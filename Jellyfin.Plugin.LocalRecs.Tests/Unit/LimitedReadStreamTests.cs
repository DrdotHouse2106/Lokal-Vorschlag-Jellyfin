using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die Größengrenze beim Lesen von Programmdateien.
    /// </summary>
    public class LimitedReadStreamTests
    {
        [Fact]
        public void ReadsUpToTheLimit()
        {
            using var limited = new LimitedReadStream(new MemoryStream(new byte[1000]), 1000);
            using var target = new MemoryStream();

            limited.CopyTo(target);

            target.Length.Should().Be(1000);
        }

        [Fact]
        public void StopsAnInflatedGzipStream()
        {
            using var packed = new MemoryStream();
            using (var gzip = new GZipStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(new byte[1024 * 1024]);
            }

            packed.Position = 0;
            using var limited = new LimitedReadStream(new GZipStream(packed, CompressionMode.Decompress), 64 * 1024);

            var act = () => limited.CopyTo(Stream.Null);

            act.Should().Throw<InvalidDataException>();
            packed.Length.Should().BeLessThan(64 * 1024, "eine kleine Datei, die beim Entpacken riesig wird");
        }
    }
}
