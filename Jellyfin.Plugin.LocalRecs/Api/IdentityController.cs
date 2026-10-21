using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Data.Queries;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Identitätsnachweis des Servers für Apps im Heimnetz: Bevor eine App ihren Zugangsschlüssel unverschlüsselt (http)
    /// an eine lokale Adresse schickt, prüft sie, dass dort wirklich ihr Server antwortet. Der Server beweist das mit
    /// einem HMAC über eine Zufallszahl der App – Schlüssel ist der Hash des Zugangsschlüssels, den nur App und echter
    /// Server kennen. Gegen Weiterleiten (ein falscher Server reicht die Anfrage an den echten durch) antwortet der
    /// Endpunkt nur bei direkter Verbindung aus dem lokalen Netz und rechnet die angesprochene Adresse (Host) mit ein.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Identity")]
    public class IdentityController : ControllerBase
    {
        /// <summary>Requests per remote address and minute.</summary>
        public const int MaxRequestsPerMinute = 30;

        /// <summary>Minimum number of proofs in an answer (filled with random values), so the answer does not show whether the device is known.</summary>
        public const int MinProofs = 4;

        private static readonly Regex NoncePattern = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        private static readonly string[] ForwardHeaders = { "X-Forwarded-For", "X-Forwarded-Host", "X-Real-IP", "Forwarded" };
        private static readonly ConcurrentDictionary<string, (long Minute, int Count)> Budget = new();

        private readonly IDeviceManager _deviceManager;
        private readonly IServerApplicationHost _appHost;
        private readonly INetworkManager _networkManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="IdentityController"/> class.
        /// </summary>
        /// <param name="deviceManager">Device manager (access tokens per device).</param>
        /// <param name="appHost">Server application host (server ID).</param>
        /// <param name="networkManager">Network manager (local networks).</param>
        public IdentityController(IDeviceManager deviceManager, IServerApplicationHost appHost, INetworkManager networkManager)
        {
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            _appHost = appHost ?? throw new ArgumentNullException(nameof(appHost));
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        }

        /// <summary>
        /// Computes the proof: hex(HMAC-SHA256(key = SHA-256(UTF-8 access token), message = UTF-8 "nonce|serverId|host")).
        /// </summary>
        /// <param name="accessToken">Access token of the device.</param>
        /// <param name="nonce">Nonce of the app (64 lower-case hex digits).</param>
        /// <param name="serverId">Server ID (lower-case, without dashes).</param>
        /// <param name="host">Host the app connected to, lower-case, e.g. "192.168.1.10:8096".</param>
        /// <returns>The proof in lower-case hex.</returns>
        public static string Proof(string accessToken, string nonce, string serverId, string host)
        {
            ArgumentNullException.ThrowIfNull(accessToken);
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
            var message = Encoding.UTF8.GetBytes(nonce + "|" + serverId + "|" + host);
            return Convert.ToHexString(HMACSHA256.HashData(key, message)).ToLowerInvariant();
        }

        /// <summary>
        /// Fills the proofs with random values up to <see cref="MinProofs"/> and shuffles them, so known and unknown
        /// devices get answers of the same form (the app only checks whether its own proof is among them).
        /// </summary>
        /// <param name="proofs">The real proofs.</param>
        /// <returns>At least <see cref="MinProofs"/> proofs in random order.</returns>
        public static IReadOnlyList<string> PadProofs(IReadOnlyList<string> proofs)
        {
            ArgumentNullException.ThrowIfNull(proofs);
            var all = proofs.ToList();
            while (all.Count < MinProofs)
            {
                all.Add(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
            }

            RandomNumberGenerator.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(all));
            return all;
        }

        /// <summary>
        /// Answers a challenge of an app in the home network. Anonymous; only for direct connections from the local network.
        /// </summary>
        /// <param name="nonce">32 random bytes of the app as 64 lower-case hex digits.</param>
        /// <param name="deviceId">Jellyfin device ID of the app (header "X-Device-Id").</param>
        /// <returns>Server ID, the host as seen by the server and one proof per access token of the device (usually one).</returns>
        [HttpGet("Challenge")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public ActionResult<IdentityProof> Challenge([FromQuery] string? nonce, [FromHeader(Name = "X-Device-Id")] string? deviceId)
        {
            Response.Headers.CacheControl = "no-store";
            var remote = HttpContext.Connection.RemoteIpAddress;
            if (remote == null || !_networkManager.IsInLocalNetwork(remote) || ForwardHeaders.Any(h => Request.Headers.ContainsKey(h)))
            {
                // Nur direkt aus dem Heimnetz – sonst könnte ein falscher Server die Anfrage durchreichen
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var minute = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
            var budget = Budget.AddOrUpdate(remote.ToString(), _ => (minute, 1), (_, old) => old.Minute == minute ? (minute, old.Count + 1) : (minute, 1));
            if (budget.Count > MaxRequestsPerMinute)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests);
            }

            if (Budget.Count > 10000)
            {
                // Nur abgelaufene Minuten entfernen – die laufenden Grenzen bleiben bestehen
                foreach (var stale in Budget.Where(b => b.Value.Minute != minute).Select(b => b.Key).ToList())
                {
                    Budget.TryRemove(stale, out _);
                }
            }

            nonce = nonce?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(nonce) || !NoncePattern.IsMatch(nonce) || string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 256)
            {
                return BadRequest();
            }

            var serverId = (_appHost.SystemId ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            var host = Request.Host.Value?.ToLowerInvariant() ?? string.Empty;
            var tokens = _deviceManager.GetDevices(new DeviceQuery { DeviceId = deviceId }).Items
                .Where(d => !string.IsNullOrEmpty(d.AccessToken))
                .Select(d => d.AccessToken)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return new IdentityProof
            {
                ServerId = serverId,
                Host = host,
                Proofs = PadProofs(tokens.Select(t => Proof(t, nonce, serverId, host)).ToList())
            };
        }

        /// <summary>
        /// Answer to a challenge.
        /// </summary>
        public class IdentityProof
        {
            /// <summary>Gets or sets the server ID (lower-case, without dashes).</summary>
            public string ServerId { get; set; } = string.Empty;

            /// <summary>Gets or sets the host as seen by the server (part of the proof; the app compares it with the address it used).</summary>
            public string Host { get; set; } = string.Empty;

            /// <summary>Gets or sets one proof per access token of the device, filled with random values to at least <see cref="MinProofs"/>.</summary>
            public IReadOnlyList<string> Proofs { get; set; } = Array.Empty<string>();
        }
    }
}
