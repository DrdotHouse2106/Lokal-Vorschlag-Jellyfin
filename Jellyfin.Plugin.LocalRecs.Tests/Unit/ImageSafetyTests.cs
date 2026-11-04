using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Security;
using Jellyfin.Plugin.LocalRecs.Worlds;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die Bildprüfungen (Welt-Logos, Radarr-Plakate): nur Rasterbilder, erlaubte Hosts, keine lokalen Adressen.
    /// </summary>
    public class ImageSafetyTests
    {
        [Theory]
        [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
        [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, "image/gif")]
        [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, "image/webp")]
        public void DetectType_RasterImages(byte[] data, string expected)
        {
            ImageSafety.DetectType(data).Should().Be(expected);
        }

        [Theory]
        [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
        [InlineData("<?xml version=\"1.0\"?><svg/>")]
        [InlineData("<html><script>alert(1)</script></html>")]
        [InlineData("")]
        public void DetectType_SvgAndHtml_AreRejected(string text)
        {
            ImageSafety.DetectType(Encoding.UTF8.GetBytes(text)).Should().BeNull();
        }

        [Theory]
        [InlineData("127.0.0.1", false)]
        [InlineData("10.1.2.3", false)]
        [InlineData("172.20.0.1", false)]
        [InlineData("192.168.72.10", false)]
        [InlineData("169.254.169.254", false)]
        [InlineData("100.64.0.1", false)]
        [InlineData("0.0.0.0", false)]
        [InlineData("::1", false)]
        [InlineData("fd00::1", false)]
        [InlineData("fe80::1", false)]
        [InlineData("::ffff:192.168.1.1", false)]
        [InlineData("151.101.1.1", true)]
        [InlineData("2606:4700::1", true)]
        public void IsPublic(string address, bool expected)
        {
            ImageSafety.IsPublic(IPAddress.Parse(address)).Should().Be(expected);
        }

        [Theory]
        [InlineData("https://image.tmdb.org/t/p/w500/a.png", true)]
        [InlineData("https://IMAGE.TMDB.ORG/t/p/w500/a.png", true)]
        [InlineData("https://image.tmdb.org.evil.example/a.png", false)]
        [InlineData("https://image.tmdb.org:8443/a.png", false)]
        [InlineData("http://image.tmdb.org/a.png", false)]
        [InlineData("https://evil.example/a.png", false)]
        public void IsAllowedHost(string url, bool expected)
        {
            ImageSafety.IsAllowedHost(new Uri(url), ImageSafety.LogoHosts).Should().Be(expected);
        }

        [Fact]
        public void WorldLogo_OnlyAllowedHostsAndTheListHost()
        {
            const string List = "https://raw.githubusercontent.com/DrdotHouse2106/Jellyfin-Welten/main/welten.json";
            WorldLogoService.Resolve("https://evil.example/logo.png", List).Should().BeNull();
            WorldLogoService.Resolve("https://192.168.1.10/logo.png", List).Should().BeNull();
            WorldLogoService.Resolve("https://assets.fanart.tv/logo.png", List).Should().Be("https://assets.fanart.tv/logo.png");
            WorldLogoService.Resolve("https://welten.example/logo.png", "https://welten.example/meine-welten.json").Should().Be("https://welten.example/logo.png", "Host der eingestellten Liste");
            WorldLogoService.Resolve("logo.svg", "/tmp/welten/welten.json").Should().BeNull("SVG ist nicht erlaubt");
        }

        [Fact]
        public async Task ReadImage_LimitAndType()
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
            (await ImageSafety.ReadImageAsync(new ByteArrayContent(png), 100, CancellationToken.None))!.Value.ContentType.Should().Be("image/png");
            (await ImageSafety.ReadImageAsync(new ByteArrayContent(png), 5, CancellationToken.None)).Should().BeNull("zu groß");
            var svg = new StringContent("<svg><script>alert(1)</script></svg>", Encoding.UTF8, "image/svg+xml");
            (await ImageSafety.ReadImageAsync(svg, 1000, CancellationToken.None)).Should().BeNull("SVG trotz image/-Typ");
        }

        [Fact]
        public async Task ResolvesToPublic_LiteralPrivateAddressIsRejected()
        {
            (await ImageSafety.ResolvesToPublicAsync(new Uri("https://127.0.0.1/x.png"), CancellationToken.None)).Should().BeFalse();
            (await ImageSafety.ResolvesToPublicAsync(new Uri("https://[::1]/x.png"), CancellationToken.None)).Should().BeFalse();
        }
    }
}
