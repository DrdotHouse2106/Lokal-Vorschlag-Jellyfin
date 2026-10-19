using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.LocalRecs.Reminders
{
    /// <summary>
    /// Kleiner QR-Code-Erzeuger (Byte-Modus, Fehlerkorrektur M) für den Kalender-Link – als SVG, ohne Fremdpaket.
    /// Aufbau nach der Norm ISO/IEC 18004 (wie die bekannte Referenz-Implementierung von Project Nayuki).
    /// </summary>
    public sealed class QrCode
    {
        private const int FormatBitsM = 0;

        // Fehlerkorrektur-Codewörter pro Block und Anzahl Blöcke für Stufe M, Index = Version (1–40)
        private static readonly int[] EccCodewordsPerBlock =
        {
            -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26,
            26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28
        };

        private static readonly int[] NumErrorCorrectionBlocks =
        {
            -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16,
            17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49
        };

        private readonly bool[,] _modules;
        private readonly bool[,] _isFunction;

        private QrCode(int version)
        {
            Version = version;
            Size = (version * 4) + 17;
            _modules = new bool[Size, Size];
            _isFunction = new bool[Size, Size];
        }

        /// <summary>Gets the version (1–40).</summary>
        public int Version { get; }

        /// <summary>Gets the width and height in modules.</summary>
        public int Size { get; }

        /// <summary>
        /// Encodes a text (UTF-8, byte mode, error correction M) with the smallest fitting version.
        /// </summary>
        /// <param name="text">The text, e.g. a URL.</param>
        /// <returns>The QR code.</returns>
        /// <exception cref="ArgumentException">Thrown when the text is too long.</exception>
        public static QrCode Encode(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            var data = Encoding.UTF8.GetBytes(text);
            for (var version = 1; version <= 40; version++)
            {
                var capacityBits = GetNumDataCodewords(version) * 8;
                var countBits = version <= 9 ? 8 : 16;
                var neededBits = 4 + countBits + (data.Length * 8);
                if (neededBits <= capacityBits)
                {
                    var qr = new QrCode(version);
                    qr.Build(data, countBits, capacityBits);
                    return qr;
                }
            }

            throw new ArgumentException("Text ist zu lang für einen QR-Code.", nameof(text));
        }

        /// <summary>
        /// Gets a module (true = dark).
        /// </summary>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <returns>True if dark; false outside the code.</returns>
        public bool GetModule(int x, int y)
        {
            return x >= 0 && x < Size && y >= 0 && y < Size && _modules[y, x];
        }

        /// <summary>
        /// Renders the code as SVG with a quiet zone of 4 modules.
        /// </summary>
        /// <returns>The SVG document.</returns>
        public string ToSvg()
        {
            const int border = 4;
            var dimension = Size + (border * 2);
            var path = new StringBuilder();
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    if (_modules[y, x])
                    {
                        path.Append(CultureInfo.InvariantCulture, $"M{x + border},{y + border}h1v1h-1z");
                    }
                }
            }

            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                + "<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" viewBox=\"0 0 " + dimension.ToString(CultureInfo.InvariantCulture) + " " + dimension.ToString(CultureInfo.InvariantCulture) + "\" shape-rendering=\"crispEdges\">"
                + "<rect width=\"100%\" height=\"100%\" fill=\"#FFFFFF\"/>"
                + "<path d=\"" + path + "\" fill=\"#000000\"/></svg>";
        }

        private static int GetNumRawDataModules(int version)
        {
            var result = (((16 * version) + 128) * version) + 64;
            if (version >= 2)
            {
                var numAlign = (version / 7) + 2;
                result -= (((25 * numAlign) - 10) * numAlign) - 55;
                if (version >= 7)
                {
                    result -= 36;
                }
            }

            return result;
        }

        private static int GetNumDataCodewords(int version)
        {
            return (GetNumRawDataModules(version) / 8) - (EccCodewordsPerBlock[version] * NumErrorCorrectionBlocks[version]);
        }

        private static int Multiply(int x, int y)
        {
            var z = 0;
            for (var i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }

            return z;
        }

        private static int[] ReedSolomonDivisor(int degree)
        {
            var result = new int[degree];
            result[degree - 1] = 1;
            var root = 1;
            for (var i = 0; i < degree; i++)
            {
                for (var j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < degree)
                    {
                        result[j] ^= result[j + 1];
                    }
                }

                root = Multiply(root, 0x02);
            }

            return result;
        }

        private static int[] ReedSolomonRemainder(IReadOnlyList<int> data, int[] divisor)
        {
            var result = new int[divisor.Length];
            foreach (var b in data)
            {
                var factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[^1] = 0;
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] ^= Multiply(divisor[i], factor);
                }
            }

            return result;
        }

        private static bool GetBit(int value, int index) => ((value >> index) & 1) != 0;

        private void Build(byte[] data, int countBits, int capacityBits)
        {
            // Bitstrom: Modus „Byte“, Länge, Daten, Abschluss, Füllbytes
            var bits = new List<bool>();
            void Append(int value, int length)
            {
                for (var i = length - 1; i >= 0; i--)
                {
                    bits.Add(GetBit(value, i));
                }
            }

            Append(0x4, 4);
            Append(data.Length, countBits);
            foreach (var b in data)
            {
                Append(b, 8);
            }

            Append(0, Math.Min(4, capacityBits - bits.Count));
            Append(0, (8 - (bits.Count % 8)) % 8);
            for (var pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
            {
                Append(pad, 8);
            }

            var codewords = new int[bits.Count / 8];
            for (var i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                {
                    codewords[i >> 3] |= 1 << (7 - (i & 7));
                }
            }

            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(codewords));

            var bestMask = 0;
            var bestPenalty = int.MaxValue;
            for (var mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                var penalty = Penalty();
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                }

                ApplyMask(mask);
            }

            ApplyMask(bestMask);
            DrawFormatBits(bestMask);
        }

        private int[] AddEccAndInterleave(int[] data)
        {
            var numBlocks = NumErrorCorrectionBlocks[Version];
            var blockEccLen = EccCodewordsPerBlock[Version];
            var rawCodewords = GetNumRawDataModules(Version) / 8;
            var numShortBlocks = numBlocks - (rawCodewords % numBlocks);
            var shortBlockLen = rawCodewords / numBlocks;

            var blocks = new List<int[]>();
            var divisor = ReedSolomonDivisor(blockEccLen);
            var k = 0;
            for (var i = 0; i < numBlocks; i++)
            {
                var datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
                var dat = new int[datLen];
                Array.Copy(data, k, dat, 0, datLen);
                k += datLen;
                var ecc = ReedSolomonRemainder(dat, divisor);
                var block = new int[shortBlockLen + 1];
                Array.Copy(dat, 0, block, 0, datLen);
                if (i < numShortBlocks)
                {
                    // Platzhalter, damit alle Blöcke gleich lang sind (wird beim Verschachteln übersprungen)
                    Array.Copy(ecc, 0, block, datLen + 1, ecc.Length);
                }
                else
                {
                    Array.Copy(ecc, 0, block, datLen, ecc.Length);
                }

                blocks.Add(block);
            }

            var result = new List<int>();
            for (var i = 0; i < shortBlockLen + 1; i++)
            {
                for (var j = 0; j < blocks.Count; j++)
                {
                    if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                    {
                        result.Add(blocks[j][i]);
                    }
                }
            }

            return result.ToArray();
        }

        private void SetFunction(int x, int y, bool dark)
        {
            _modules[y, x] = dark;
            _isFunction[y, x] = true;
        }

        private void DrawFunctionPatterns()
        {
            for (var i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }

            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);

            var positions = AlignmentPositions();
            var last = positions.Count - 1;
            for (var i = 0; i < positions.Count; i++)
            {
                for (var j = 0; j < positions.Count; j++)
                {
                    if (!((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0)))
                    {
                        DrawAlignment(positions[i], positions[j]);
                    }
                }
            }

            DrawFormatBits(0);
            DrawVersion();
        }

        private void DrawFinder(int x, int y)
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    var xx = x + dx;
                    var yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size)
                    {
                        SetFunction(xx, yy, dist != 2 && dist != 4);
                    }
                }
            }
        }

        private void DrawAlignment(int x, int y)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        private List<int> AlignmentPositions()
        {
            var result = new List<int>();
            if (Version == 1)
            {
                return result;
            }

            var numAlign = (Version / 7) + 2;
            var step = Version == 32 ? 26 : (int)Math.Ceiling(((Version * 4) + 4) / (double)((numAlign * 2) - 2)) * 2;
            result.Add(6);
            for (var pos = Size - 7; result.Count < numAlign; pos -= step)
            {
                result.Insert(1, pos);
            }

            return result;
        }

        private void DrawFormatBits(int mask)
        {
            var data = (FormatBitsM << 3) | mask;
            var rem = data;
            for (var i = 0; i < 10; i++)
            {
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            }

            var bits = ((data << 10) | rem) ^ 0x5412;
            for (var i = 0; i <= 5; i++)
            {
                SetFunction(8, i, GetBit(bits, i));
            }

            SetFunction(8, 7, GetBit(bits, 6));
            SetFunction(8, 8, GetBit(bits, 7));
            SetFunction(7, 8, GetBit(bits, 8));
            for (var i = 9; i < 15; i++)
            {
                SetFunction(14 - i, 8, GetBit(bits, i));
            }

            for (var i = 0; i < 8; i++)
            {
                SetFunction(Size - 1 - i, 8, GetBit(bits, i));
            }

            for (var i = 8; i < 15; i++)
            {
                SetFunction(8, Size - 15 + i, GetBit(bits, i));
            }

            SetFunction(8, Size - 8, true);
        }

        private void DrawVersion()
        {
            if (Version < 7)
            {
                return;
            }

            var rem = Version;
            for (var i = 0; i < 12; i++)
            {
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            }

            var bits = (Version << 12) | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = GetBit(bits, i);
                var a = Size - 11 + (i % 3);
                var b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        private void DrawCodewords(int[] data)
        {
            var i = 0;
            for (var right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5;
                }

                for (var vert = 0; vert < Size; vert++)
                {
                    for (var j = 0; j < 2; j++)
                    {
                        var x = right - j;
                        var upward = ((right + 1) & 2) == 0;
                        var y = upward ? Size - 1 - vert : vert;
                        if (!_isFunction[y, x] && i < data.Length * 8)
                        {
                            _modules[y, x] = GetBit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
                }
            }
        }

        private void ApplyMask(int mask)
        {
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    bool invert = mask switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => ((x / 3) + (y / 2)) % 2 == 0,
                        5 => ((x * y) % 2) + ((x * y) % 3) == 0,
                        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
                        _ => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0
                    };
                    if (invert && !_isFunction[y, x])
                    {
                        _modules[y, x] = !_modules[y, x];
                    }
                }
            }
        }

        // Vereinfachte Bewertung: gleichfarbige Reihen, 2×2-Blöcke und das Verhältnis hell/dunkel
        private int Penalty()
        {
            var result = 0;
            var dark = 0;
            for (var y = 0; y < Size; y++)
            {
                var runRow = 1;
                var runCol = 1;
                for (var x = 0; x < Size; x++)
                {
                    if (_modules[y, x])
                    {
                        dark++;
                    }

                    if (x > 0)
                    {
                        runRow = _modules[y, x] == _modules[y, x - 1] ? runRow + 1 : 1;
                        runCol = _modules[x, y] == _modules[x - 1, y] ? runCol + 1 : 1;
                        result += runRow == 5 ? 3 : runRow > 5 ? 1 : 0;
                        result += runCol == 5 ? 3 : runCol > 5 ? 1 : 0;
                    }

                    if (x > 0 && y > 0 && _modules[y, x] == _modules[y, x - 1] && _modules[y, x] == _modules[y - 1, x] && _modules[y, x] == _modules[y - 1, x - 1])
                    {
                        result += 3;
                    }
                }
            }

            var total = Size * Size;
            var k = (int)Math.Ceiling(Math.Abs((dark * 20) - (total * 10)) / (double)total) - 1;
            return result + (Math.Max(0, k) * 10);
        }
    }
}
