using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Similarity
{
    /// <summary>
    /// Persönliche Trakt-Empfehlungen (<c>/recommendations/movies</c> und <c>/shows</c>) für Benutzer mit Trakt-Konto.
    /// Zugang in dieser Reihenfolge: eigene Verbindung (Anmeldung per Code in Quallenkino, braucht eine eigene Trakt-App)
    /// oder – nur lesend – die Verknüpfung des Trakt-Plugins (<c>Trakt.xml</c>). Dessen Zugang wird nie erneuert
    /// oder zurückgeschrieben. Gesendet wird nur der Abruf der Empfehlungen; das Scrobbeln macht weiter das Trakt-Plugin.
    /// </summary>
    public class TraktPersonalService : IDisposable
    {
        /// <summary>Source of the access: own connection in Quallenkino.</summary>
        public const string SourceOwn = "eigen";

        /// <summary>Source of the access: link of the Trakt plugin (read only).</summary>
        public const string SourcePlugin = "trakt-plugin";

        private const string TraktBase = "https://api.trakt.tv/";
        private const string RedirectUri = "urn:ietf:wg:oauth:2.0:oob";
        private static readonly TimeSpan CacheTime = TimeSpan.FromHours(12);

        private static readonly HttpClient SharedClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly ILogger<TraktPersonalService> _logger;
        private readonly string _storePath;
        private readonly string _traktPluginConfigPath;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<Guid, (DateTime Fetched, PersonalRecommendations Recommendations)> _cache = new ConcurrentDictionary<Guid, (DateTime Fetched, PersonalRecommendations Recommendations)>();
        private readonly ConcurrentDictionary<Guid, DeviceLogin> _logins = new ConcurrentDictionary<Guid, DeviceLogin>();
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _polls = new ConcurrentDictionary<Guid, CancellationTokenSource>();
        private Dictionary<string, Account>? _accounts;

        /// <summary>
        /// Initializes a new instance of the <see cref="TraktPersonalService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="storePath">File with the per-user settings and own connections (contains secrets).</param>
        /// <param name="traktPluginConfigPath">Configuration file of the Trakt plugin (read only).</param>
        /// <param name="send">Sends a request (for tests).</param>
        public TraktPersonalService(
            ILogger<TraktPersonalService> logger,
            string storePath,
            string traktPluginConfigPath,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storePath = storePath ?? throw new ArgumentNullException(nameof(storePath));
            _traktPluginConfigPath = traktPluginConfigPath ?? throw new ArgumentNullException(nameof(traktPluginConfigPath));
            _send = send ?? ((request, token) => SharedClient.SendAsync(request, token));
        }

        /// <summary>
        /// Gets or sets the client ID of the Trakt plugin (normally read from its assembly; settable for tests).
        /// </summary>
        public string? PluginClientIdOverride { get; set; }

        /// <summary>
        /// Reads the access of a Jellyfin user from the Trakt plugin's configuration (read only).
        /// </summary>
        /// <param name="xml">Content of Trakt.xml.</param>
        /// <param name="userId">Jellyfin user.</param>
        /// <returns>The access token, or null.</returns>
        public static string? ReadPluginToken(string xml, Guid userId)
        {
            try
            {
                var document = XDocument.Parse(xml);
                foreach (var user in document.Descendants().Where(e => string.Equals(e.Name.LocalName, "TraktUser", StringComparison.OrdinalIgnoreCase)))
                {
                    string? Value(string name) => user.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;
                    if (Guid.TryParse(Value("LinkedMbUserId"), out var linked) && linked == userId)
                    {
                        var token = Value("AccessToken");
                        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
                    }
                }
            }
            catch (XmlException)
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Gets the state for a user.
        /// </summary>
        /// <param name="userId">Jellyfin user.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <returns>The state.</returns>
        public TraktStatus GetStatus(Guid userId, PluginConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(config);
            var account = GetAccount(userId);
            var source = ResolveSource(userId, config, account);
            _logins.TryGetValue(userId, out var login);
            return new TraktStatus
            {
                FeatureEnabled = config.EnableTraktPersonal,
                Enabled = account?.Enabled == true,
                Source = source,
                CanConnect = HasOwnApp(config),
                OwnConnected = !string.IsNullOrEmpty(account?.AccessToken),
                PluginInstalled = (PluginClientIdOverride ?? PluginClientId()) != null,
                PluginLinked = source == SourcePlugin,
                UserCode = login != null && !login.Done && login.ExpiresAt > DateTime.UtcNow ? login.UserCode : null,
                VerificationUrl = login != null && !login.Done && login.ExpiresAt > DateTime.UtcNow ? login.VerificationUrl : null,
                LoginError = login?.Error
            };
        }

        /// <summary>
        /// Switches the personal recommendations on or off for a user.
        /// </summary>
        /// <param name="userId">Jellyfin user.</param>
        /// <param name="enabled">On or off.</param>
        public void SetEnabled(Guid userId, bool enabled)
        {
            UpdateAccount(userId, a => a.Enabled = enabled);
            _cache.TryRemove(userId, out _);
        }

        /// <summary>
        /// Starts the login with a code (device flow) for an own connection; the server waits for the confirmation in the background.
        /// </summary>
        /// <param name="userId">Jellyfin user.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The state with code and address, or an error.</returns>
        public async Task<TraktStatus> StartLoginAsync(Guid userId, PluginConfiguration config, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (!HasOwnApp(config))
            {
                return GetStatus(userId, config);
            }

            var clientId = config.TraktClientId.Trim();
            using var request = JsonRequest(HttpMethod.Post, "oauth/device/code", new Dictionary<string, string> { ["client_id"] = clientId });
            try
            {
                using var response = await _send(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logins[userId] = new DeviceLogin { Error = "Trakt lehnt die Anmeldung ab (HTTP " + (int)response.StatusCode + ") – Client-ID prüfen", Done = true };
                    return GetStatus(userId, config);
                }

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                var root = document.RootElement;
                var login = new DeviceLogin
                {
                    DeviceCode = root.GetProperty("device_code").GetString() ?? string.Empty,
                    UserCode = root.GetProperty("user_code").GetString() ?? string.Empty,
                    VerificationUrl = root.TryGetProperty("verification_url", out var verification) ? verification.GetString() ?? "https://trakt.tv/activate" : "https://trakt.tv/activate",
                    ExpiresAt = DateTime.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 600),
                    Interval = TimeSpan.FromSeconds(Math.Max(1, root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5))
                };
                _logins[userId] = login;
                CancelLogin(userId);
                var poll = new CancellationTokenSource();
                _polls[userId] = poll;
                _ = PollLoginAsync(userId, login, clientId, config.TraktClientSecret.Trim(), poll.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException
                || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logins[userId] = new DeviceLogin { Error = "Trakt ist nicht erreichbar", Done = true };
            }

            return GetStatus(userId, config);
        }

        /// <summary>
        /// Removes the own connection of a user (and revokes it at Trakt). The link of the Trakt plugin stays untouched.
        /// </summary>
        /// <param name="userId">Jellyfin user.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task DisconnectAsync(Guid userId, PluginConfiguration config, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(config);
            CancelLogin(userId);
            var token = GetAccount(userId)?.AccessToken;
            UpdateAccount(userId, a =>
            {
                a.AccessToken = null;
                a.RefreshToken = null;
                a.ExpiresAt = null;
            });
            _cache.TryRemove(userId, out _);
            if (string.IsNullOrEmpty(token) || !HasOwnApp(config))
            {
                return;
            }

            try
            {
                using var request = JsonRequest(HttpMethod.Post, "oauth/revoke", new Dictionary<string, string>
                {
                    ["token"] = token,
                    ["client_id"] = config.TraktClientId.Trim(),
                    ["client_secret"] = config.TraktClientSecret.Trim()
                });
                using var response = await _send(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Lokal ist die Verbindung entfernt; bei Trakt läuft der Zugang dann von selbst ab
            }
        }

        /// <summary>
        /// Gets the personal recommendations of a user (cached for 12 hours), or null if off, not linked or on errors.
        /// </summary>
        /// <param name="userId">Jellyfin user.</param>
        /// <param name="config">Plugin configuration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The recommendations, or null.</returns>
        public async Task<PersonalRecommendations?> GetRecommendationsAsync(Guid userId, PluginConfiguration config, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (!config.EnableTraktPersonal || config.TraktPersonalWeight <= 0)
            {
                return null;
            }

            var account = GetAccount(userId);

            // Nur nach ausdrücklichem Einschalten durch den Benutzer (auch die Mitnutzung der Trakt-Plugin-Verknüpfung)
            if (account?.Enabled != true)
            {
                return null;
            }

            if (_cache.TryGetValue(userId, out var cached) && DateTime.UtcNow - cached.Fetched < CacheTime)
            {
                return cached.Recommendations;
            }

            var access = await ResolveAccessAsync(userId, config, account, cancellationToken).ConfigureAwait(false);
            if (access == null)
            {
                return null;
            }

            var movies = await FetchAsync("recommendations/movies?limit=50&ignore_collected=false", access.Value, userId, cancellationToken).ConfigureAwait(false);
            var shows = await FetchAsync("recommendations/shows?limit=50&ignore_collected=false", access.Value, userId, cancellationToken).ConfigureAwait(false);
            if (movies == null && shows == null)
            {
                return null;
            }

            var result = new PersonalRecommendations
            {
                Movies = movies ?? Array.Empty<string>(),
                Shows = shows ?? Array.Empty<string>(),
                Source = access.Value.Source
            };
            _cache[userId] = (DateTime.UtcNow, result);
            return result;
        }

        /// <summary>
        /// Stops all running login polls (server shutdown).
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Stops all running login polls.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            foreach (var userId in _polls.Keys.ToList())
            {
                CancelLogin(userId);
            }
        }

        private static bool HasOwnApp(PluginConfiguration config)
            => !string.IsNullOrWhiteSpace(config.TraktClientId) && !string.IsNullOrWhiteSpace(config.TraktClientSecret);

        private static HttpRequestMessage JsonRequest(HttpMethod method, string path, Dictionary<string, string> body)
        {
            var request = new HttpRequestMessage(method, new Uri(TraktBase + path))
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private static string? PluginClientId()
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "Trakt", StringComparison.Ordinal));
                var value = assembly?.GetType("Trakt.Api.TraktUris")?.GetField("ClientId", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is TypeLoadException || ex is FileNotFoundException || ex is BadImageFormatException
                || ex is InvalidOperationException || ex is NotSupportedException)
            {
                return null;
            }
        }

        // Schreibt eine Datei, die nur der Besitzer lesen und schreiben darf (unter Windows gelten die Ordnerrechte)
        private static void WritePrivate(string path, string content)
        {
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllText(path, content);
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            };
            using (var stream = new FileStream(path, options))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
            }

            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        private string? ResolveSource(Guid userId, PluginConfiguration config, Account? account)
        {
            if (!string.IsNullOrEmpty(account?.AccessToken) && HasOwnApp(config))
            {
                return SourceOwn;
            }

            // Trakt.xml wird erst gelesen, wenn der Benutzer es eingeschaltet hat
            return account?.Enabled == true && (PluginClientIdOverride ?? PluginClientId()) != null && ReadPluginTokenFromFile(userId) != null ? SourcePlugin : null;
        }

        private async Task<(string Token, string ClientId, string Source)?> ResolveAccessAsync(Guid userId, PluginConfiguration config, Account? account, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(account?.AccessToken) && HasOwnApp(config))
            {
                var token = account.AccessToken;
                if (account.ExpiresAt.HasValue && account.ExpiresAt.Value - DateTime.UtcNow < TimeSpan.FromHours(1) && !string.IsNullOrEmpty(account.RefreshToken))
                {
                    token = await RefreshOwnAsync(userId, account.RefreshToken, config, cancellationToken).ConfigureAwait(false) ?? token;
                }

                return (token, config.TraktClientId.Trim(), SourceOwn);
            }

            var pluginClient = PluginClientIdOverride ?? PluginClientId();
            var pluginToken = pluginClient != null ? ReadPluginTokenFromFile(userId) : null;
            return pluginToken != null ? (pluginToken, pluginClient!, SourcePlugin) : null;
        }

        private string? ReadPluginTokenFromFile(Guid userId)
        {
            try
            {
                return File.Exists(_traktPluginConfigPath) ? ReadPluginToken(File.ReadAllText(_traktPluginConfigPath), userId) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private async Task<string?> RefreshOwnAsync(Guid userId, string refreshToken, PluginConfiguration config, CancellationToken cancellationToken)
        {
            try
            {
                using var request = JsonRequest(HttpMethod.Post, "oauth/token", new Dictionary<string, string>
                {
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = config.TraktClientId.Trim(),
                    ["client_secret"] = config.TraktClientSecret.Trim(),
                    ["redirect_uri"] = RedirectUri,
                    ["grant_type"] = "refresh_token"
                });
                using var response = await _send(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return StoreTokens(userId, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                return null;
            }
        }

        private string? StoreTokens(Guid userId, string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            if (string.IsNullOrEmpty(access))
            {
                return null;
            }

            var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            var expires = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? DateTime.UtcNow.AddSeconds(e.GetInt64()) : (DateTime?)null;
            UpdateAccount(userId, account =>
            {
                account.AccessToken = access;
                account.RefreshToken = refresh;
                account.ExpiresAt = expires;
            });
            return access;
        }

        // Bricht das Abfragen einer laufenden Anmeldung ab (neue Anmeldung, Trennen). Nur abbrechen, nicht freigeben:
        // die Abfrage hält das Token noch; ohne Zeitgeber braucht die Quelle kein Dispose.
        private void CancelLogin(Guid userId)
        {
            if (_polls.TryRemove(userId, out var running))
            {
                running.Cancel();
            }
        }

        private async Task PollLoginAsync(Guid userId, DeviceLogin login, string clientId, string clientSecret, CancellationToken cancellationToken)
        {
            var interval = login.Interval;
            while (DateTime.UtcNow < login.ExpiresAt && !login.Done)
            {
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // neue Anmeldung, Trennen oder Server fährt herunter
                    login.Done = true;
                    return;
                }

                try
                {
                    using var request = JsonRequest(HttpMethod.Post, "oauth/device/token", new Dictionary<string, string>
                    {
                        ["code"] = login.DeviceCode,
                        ["client_id"] = clientId,
                        ["client_secret"] = clientSecret
                    });
                    using var response = await _send(request, cancellationToken).ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    if (response.IsSuccessStatusCode)
                    {
                        StoreTokens(userId, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        UpdateAccount(userId, a => a.Enabled = true);
                        _cache.TryRemove(userId, out _);
                        login.Done = true;
                        _logger.LogInformation("Trakt-Empfehlungen: Konto für Benutzer {UserId} verbunden", userId);
                        return;
                    }

                    if (status == 400)
                    {
                        continue;
                    }

                    if (status == 429)
                    {
                        interval += TimeSpan.FromSeconds(1);
                        continue;
                    }

                    login.Error = status switch
                    {
                        410 => "Der Code ist abgelaufen – bitte neu starten",
                        418 => "Die Anmeldung wurde bei Trakt abgelehnt",
                        _ => "Anmeldung fehlgeschlagen (HTTP " + status + ")"
                    };
                    login.Done = true;
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || ex is TaskCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        login.Done = true;
                        return;
                    }

                    // nächster Versuch
                }
            }

            if (!login.Done)
            {
                login.Error = "Der Code ist abgelaufen – bitte neu starten";
                login.Done = true;
            }
        }

        private async Task<IReadOnlyList<string>?> FetchAsync(string path, (string Token, string ClientId, string Source) access, Guid userId, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(TraktBase + path));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
            request.Headers.Add("trakt-api-version", "2");
            request.Headers.Add("trakt-api-key", access.ClientId);
            try
            {
                using var response = await _send(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug(
                        "Trakt-Empfehlungen für Benutzer {UserId} nicht abrufbar (HTTP {Status}, Zugang: {Source})",
                        userId,
                        (int)response.StatusCode,
                        access.Source);
                    return null;
                }

                return SimilarityParser.ParseTrakt(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogDebug("Trakt-Empfehlungen für Benutzer {UserId} nicht abrufbar: {Message}", userId, ex.Message);
                return null;
            }
        }

        private Account? GetAccount(Guid userId)
        {
            lock (_lock)
            {
                LoadStore();
                return _accounts!.TryGetValue(userId.ToString("N"), out var account) ? account : null;
            }
        }

        private void UpdateAccount(Guid userId, Action<Account> change)
        {
            string json;
            lock (_lock)
            {
                LoadStore();
                var key = userId.ToString("N");
                if (!_accounts!.TryGetValue(key, out var account))
                {
                    _accounts[key] = account = new Account();
                }

                change(account);
                json = JsonSerializer.Serialize(_accounts);
            }

            try
            {
                var directory = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Enthält Zugangsschlüssel: nur für den Jellyfin-Benutzer lesbar (600)
                var temp = _storePath + ".tmp";
                WritePrivate(temp, json);
                File.Move(temp, _storePath, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("Trakt-Einstellungen konnten nicht gespeichert werden: {Message}", ex.Message);
            }
        }

        private void LoadStore()
        {
            if (_accounts != null)
            {
                return;
            }

            _accounts = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(_storePath))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, Account>>(File.ReadAllText(_storePath));
                    if (loaded != null)
                    {
                        _accounts = new Dictionary<string, Account>(loaded, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _logger.LogWarning("Trakt-Einstellungen konnten nicht gelesen werden: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Settings and own connection of a user (stored in the plugin's data folder).
        /// </summary>
        public class Account
        {
            /// <summary>Gets or sets a value indicating whether the personal recommendations are used (null = default on).</summary>
            public bool? Enabled { get; set; }

            /// <summary>Gets or sets the access token of the own connection.</summary>
            public string? AccessToken { get; set; }

            /// <summary>Gets or sets the refresh token of the own connection.</summary>
            public string? RefreshToken { get; set; }

            /// <summary>Gets or sets when the access token expires (UTC).</summary>
            public DateTime? ExpiresAt { get; set; }
        }

        private sealed class DeviceLogin
        {
            public string DeviceCode { get; set; } = string.Empty;

            public string UserCode { get; set; } = string.Empty;

            public string VerificationUrl { get; set; } = string.Empty;

            public DateTime ExpiresAt { get; set; }

            public TimeSpan Interval { get; set; }

            public bool Done { get; set; }

            public string? Error { get; set; }
        }
    }
}
