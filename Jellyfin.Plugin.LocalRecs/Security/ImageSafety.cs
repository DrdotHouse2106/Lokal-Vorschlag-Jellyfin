using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.LocalRecs.Security
{
    /// <summary>
    /// Prüfungen für Bilder, die der Server für Apps ausliefert (Welt-Logos, Radarr-Plakate): nur Rasterbilder
    /// (PNG, JPEG, WebP, GIF – erkannt an den ersten Bytes, nicht am Content-Type; SVG mit Skript fällt so heraus),
    /// Größenlimit beim Lesen, und für Adressen aus Listen: nur erlaubte Hosts, keine privaten oder lokalen Adressen.
    /// </summary>
    public static class ImageSafety
    {
        /// <summary>Hosts logos may be loaded from (besides the host of a configured world list).</summary>
        public static readonly IReadOnlyList<string> LogoHosts = new[] { "image.tmdb.org", "assets.fanart.tv", "raw.githubusercontent.com" };

        /// <summary>Headers for images served by the plugin: no sniffing, no scripts even when opened directly.</summary>
        public static readonly IReadOnlyDictionary<string, string> ResponseHeaders = new Dictionary<string, string>
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["Content-Security-Policy"] = "default-src 'none'; sandbox"
        };

        /// <summary>
        /// Gets the image type from the first bytes: „image/png“, „image/jpeg“, „image/gif“, „image/webp“, else null.
        /// </summary>
        /// <param name="data">The bytes.</param>
        /// <returns>The MIME type, or null if it is no allowed raster image.</returns>
        public static string? DetectType(ReadOnlySpan<byte> data)
        {
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            {
                return "image/png";
            }

            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                return "image/jpeg";
            }

            if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8' && (data[4] == (byte)'7' || data[4] == (byte)'9') && data[5] == (byte)'a')
            {
                return "image/gif";
            }

            if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
                && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            {
                return "image/webp";
            }

            return null;
        }

        /// <summary>
        /// Gets a value indicating whether an address is public (not loopback, private, link-local, CGNAT, multicast,
        /// unspecified or an IPv6 unique-local/site-local address; IPv4-mapped addresses are checked as IPv4).
        /// </summary>
        /// <param name="address">The address.</param>
        /// <returns>True if public.</returns>
        public static bool IsPublic(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
            {
                return false;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = address.GetAddressBytes();
                return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                    || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                    || (b[0] == 169 && b[1] == 254)
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                    || (b[0] == 198 && (b[1] == 18 || b[1] == 19)));
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = address.GetAddressBytes();
                return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC);
            }

            return false;
        }

        /// <summary>
        /// Gets a value indicating whether an https URL points to one of the allowed hosts (exact name, case-insensitive).
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="hosts">Allowed hosts.</param>
        /// <returns>True if allowed.</returns>
        public static bool IsAllowedHost(Uri url, IEnumerable<string> hosts)
        {
            ArgumentNullException.ThrowIfNull(url);
            ArgumentNullException.ThrowIfNull(hosts);
            return url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && hosts.Any(h => string.Equals(h, url.IdnHost, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Resolves the host and checks that every address is public (protection against lists pointing at the local network).
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if all addresses are public.</returns>
        public static async Task<bool> ResolvesToPublicAsync(Uri url, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(url);
            if (IPAddress.TryParse(url.IdnHost.Trim('[', ']'), out var literal))
            {
                return IsPublic(literal);
            }

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(url.IdnHost, cancellationToken).ConfigureAwait(false);
                return addresses.Length > 0 && addresses.All(IsPublic);
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads at most <paramref name="maxBytes"/> bytes of a response and returns them with the detected image type.
        /// </summary>
        /// <param name="content">The response content.</param>
        /// <param name="maxBytes">Size limit.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Bytes and type, or null if too big or no allowed image.</returns>
        public static async Task<(byte[] Data, string ContentType)?> ReadImageAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (content.Headers.ContentLength > maxBytes)
            {
                return null;
            }

            using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                memory.Write(buffer, 0, read);
                if (memory.Length > maxBytes)
                {
                    return null;
                }
            }

            var data = memory.ToArray();
            var type = DetectType(data);
            return type == null ? null : (data, type);
        }
    }
}
