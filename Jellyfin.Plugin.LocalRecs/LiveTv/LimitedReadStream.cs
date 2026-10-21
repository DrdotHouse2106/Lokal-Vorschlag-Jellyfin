using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.LocalRecs.LiveTv
{
    /// <summary>
    /// Liest höchstens eine bestimmte Menge aus einem anderen Datenstrom und bricht dann mit
    /// <see cref="InvalidDataException"/> ab – Schutz vor riesigen oder absichtlich aufgeblähten (gzip) Programmdateien.
    /// </summary>
    public sealed class LimitedReadStream : Stream
    {
        /// <summary>Maximum size of an XMLTV guide after unpacking (large real guides with many channels reach 100 MB).</summary>
        public const long MaxXmltvBytes = 250L * 1024 * 1024;

        private readonly Stream _inner;
        private readonly long _limit;
        private readonly bool _leaveOpen;
        private long _read;

        /// <summary>
        /// Initializes a new instance of the <see cref="LimitedReadStream"/> class.
        /// </summary>
        /// <param name="inner">The stream to read from.</param>
        /// <param name="limit">Maximum number of bytes.</param>
        /// <param name="leaveOpen">True to leave the inner stream open on dispose.</param>
        public LimitedReadStream(Stream inner, long limit, bool leaveOpen = false)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _limit = limit;
            _leaveOpen = leaveOpen;
        }

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => Count(_inner.Read(buffer, offset, count));

        /// <inheritdoc />
        public override int Read(Span<byte> buffer) => Count(_inner.Read(buffer));

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            _read += read;
            if (_read > _limit)
            {
                throw new InvalidDataException("XMLTV-Daten größer als " + (_limit / (1024 * 1024)) + " MB – abgebrochen");
            }

            return read;
        }
    }
}
