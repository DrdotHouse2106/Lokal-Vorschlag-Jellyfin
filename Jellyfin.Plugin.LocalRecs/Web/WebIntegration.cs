using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Web
{
    /// <summary>
    /// Hängt das Web-Modul (Startseite im Quallenkino-Stil, Quallenkino-Farben) über das Plugin „File Transformation“
    /// in die index.html von Jellyfin Web ein. Ob es aktiv ist, entscheidet das Skript anhand der Einstellungen,
    /// so wirken Änderungen ohne Neustart.
    /// </summary>
    public class WebIntegration : IHostedService
    {
        /// <summary>Marker in the injected loader (prevents double injection).</summary>
        public const string Marker = "LocalRecs/Web/localrecs-web";

        private const string TransformationId = "5f0b4a6e-1d2c-4b8e-9c3a-7e6d5c4b3a21";
        private const int MaxAttempts = 30;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

        private readonly ILogger<WebIntegration> _logger;
        private readonly CancellationTokenSource _stoppingCts = new CancellationTokenSource();

        /// <summary>
        /// Initializes a new instance of the <see cref="WebIntegration"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        public WebIntegration(ILogger<WebIntegration> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Gets a value indicating whether the transformation is registered with File Transformation.</summary>
        public static bool Registered { get; private set; }

        /// <summary>Gets the version used for cache busting (plugin version).</summary>
        public static string Version => typeof(WebIntegration).Assembly.GetName().Version?.ToString() ?? "0";

        /// <summary>
        /// Adds the loader for the web module to index.html (callback of File Transformation).
        /// </summary>
        /// <param name="payload">The file contents.</param>
        /// <returns>The changed contents.</returns>
        public static string TransformIndex(TransformationPayload payload)
        {
            var contents = payload?.Contents ?? string.Empty;
            return InjectLoader(contents, Version);
        }

        /// <summary>
        /// Inserts the loader before &lt;/head&gt;. The loader finds the server's base path itself (works with a base URL)
        /// and loads script and stylesheet with the version as query, so apps (e.g. the iOS app) do not keep old files.
        /// </summary>
        /// <param name="html">index.html.</param>
        /// <param name="version">Version for cache busting.</param>
        /// <returns>The changed HTML; unchanged if the loader is already there or there is no &lt;/head&gt;.</returns>
        public static string InjectLoader(string html, string version)
        {
            ArgumentNullException.ThrowIfNull(html);
            var index = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (index < 0 || html.Contains(Marker, StringComparison.Ordinal))
            {
                return html;
            }

            var v = Uri.EscapeDataString(version ?? "0");
            var loader = "<script>/* Quallenkinoplugin */(function(){var p=location.pathname,i=p.indexOf('/web/'),b=i>=0?p.slice(0,i):'',v='" + v + "';"
                + "var l=document.createElement('link');l.rel='stylesheet';l.href=b+'/" + Marker + ".css?v='+v;document.head.appendChild(l);"
                + "var s=document.createElement('script');s.src=b+'/" + Marker + ".js?v='+v;s.defer=true;document.head.appendChild(s);})();</script>";
            return html.Insert(index, loader);
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Task.Run(() => RegisterWithRetryAsync(_stoppingCts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _stoppingCts.Cancel();
            return Task.CompletedTask;
        }

        private static MethodInfo? FindRegisterMethod()
        {
            var assembly = AssemblyLoadContext.All
                .SelectMany(c => c.Assemblies)
                .FirstOrDefault(a => a.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);
            return assembly?.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface")?.GetMethod("RegisterTransformation");
        }

        private async Task RegisterWithRetryAsync(CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= MaxAttempts && !cancellationToken.IsCancellationRequested; attempt++)
            {
                if (TryRegister())
                {
                    return;
                }

                try
                {
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }

            _logger.LogInformation("Plugin „File Transformation“ nicht gefunden – die Startseite im Quallenkino-Stil für Jellyfin Web ist nicht verfügbar");
        }

        private bool TryRegister()
        {
            var register = FindRegisterMethod();
            var parameterType = register?.GetParameters().FirstOrDefault()?.ParameterType;
            var parse = parameterType?.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
            if (register == null || parse == null)
            {
                return false;
            }

            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    id = TransformationId,
                    fileNamePattern = "index.html",
                    callbackAssembly = typeof(WebIntegration).Assembly.FullName,
                    callbackClass = typeof(WebIntegration).FullName,
                    callbackMethod = nameof(TransformIndex)
                });
                register.Invoke(null, new[] { parse.Invoke(null, new object[] { json }) });
                Registered = true;
                _logger.LogInformation("Web-Modul (Startseite im Quallenkino-Stil) bei „File Transformation“ angemeldet");
                return true;
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
            {
                _logger.LogWarning(ex, "Web-Modul konnte nicht bei „File Transformation“ angemeldet werden");
                return false;
            }
        }

        /// <summary>
        /// Payload of File Transformation: the contents of the file.
        /// </summary>
        public class TransformationPayload
        {
            /// <summary>Gets or sets the file contents.</summary>
            [JsonPropertyName("contents")]
            public string? Contents { get; set; }
        }
    }
}
