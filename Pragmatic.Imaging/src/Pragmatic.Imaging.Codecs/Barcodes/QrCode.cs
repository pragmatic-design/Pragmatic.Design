namespace Pragmatic.Imaging.Codecs.Barcodes;

/// <summary>
///     A QR code (ISO/IEC 18004) as a module matrix: byte mode, error correction level M, the smallest
///     version that holds the data, the mask with the lowest penalty. Built the way the reference
///     encoders do it (data codewords, Reed-Solomon per block, interleaving, placement, masking, format
///     and version information), with nothing outside the standard.
/// </summary>
public sealed class QrCode
{
    private readonly bool[,] _modules;

    private QrCode(int version, int mask, bool[,] modules)
    {
        Version = version;
        Mask = mask;
        _modules = modules;
    }

    public int Version { get; }

    public int Mask { get; }

    /// <summary>Modules per side: 17 + 4 × version.</summary>
    public int Size => _modules.GetLength(0);

    /// <summary>True where the module is dark.</summary>
    public bool this[int x, int y] => _modules[y, x];

    /// <summary>Encodes text as UTF-8 bytes.</summary>
    public static QrCode Encode(string text) => Encode(System.Text.Encoding.UTF8.GetBytes(text));

    public static QrCode Encode(ReadOnlySpan<byte> data)
    {
        var version = SmallestVersion(data.Length);
        var dataCodewords = QrTables.DataCodewords(version);
        var bits = new QrBitBuffer(dataCodewords);
        bits.Append(0b0100, 4); // byte mode
        bits.Append(data.Length, version >= 10 ? 16 : 8);
        foreach (var b in data)
            bits.Append(b, 8);
        bits.Append(0, Math.Min(4, bits.Capacity - bits.Length)); // terminator
        bits.Append(0, (8 - bits.Length % 8) % 8); // to a byte boundary
        for (var pad = 0xEC; bits.Length < bits.Capacity; pad ^= 0xEC ^ 0x11)
            bits.Append(pad, 8);

        var codewords = Interleave(bits.ToBytes(), version);
        var size = 17 + 4 * version;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];
        DrawFunctionPatterns(modules, isFunction, version);
        PlaceData(modules, isFunction, codewords, version);

        var best = -1;
        var bestPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, isFunction, mask);
            DrawFormat(modules, mask);
            var penalty = QrPenalty.Score(modules);
            if (penalty < bestPenalty)
            {
                best = mask;
                bestPenalty = penalty;
            }

            ApplyMask(modules, isFunction, mask); // masking is its own inverse
        }

        ApplyMask(modules, isFunction, best);
        DrawFormat(modules, best);
        return new QrCode(version, best, modules);
    }

    private static int SmallestVersion(int byteCount)
    {
        for (var version = 1; version <= 40; version++)
        {
            var capacityBits = QrTables.DataCodewords(version) * 8;
            var needed = 4 + (version >= 10 ? 16 : 8) + byteCount * 8;
            if (needed <= capacityBits)
                return version;
        }

        throw new ArgumentException($"{byteCount} bytes do not fit a QR code (at most {QrTables.DataCodewords(40) - 3} at level M).");
    }

    /// <summary>Splits the data into blocks, appends each block's Reed-Solomon codewords, and interleaves them as the symbol wants.</summary>
    private static byte[] Interleave(byte[] data, int version)
    {
        var (ecPerBlock, group1Blocks, group1Data, group2Blocks, group2Data) = QrTables.Blocks(version);
        var blockCount = group1Blocks + group2Blocks;
        var blocks = new byte[blockCount][];
        var ecs = new byte[blockCount][];
        var offset = 0;
        var generator = ReedSolomon.Generator(ecPerBlock);
        for (var b = 0; b < blockCount; b++)
        {
            var length = b < group1Blocks ? group1Data : group2Data;
            blocks[b] = data.AsSpan(offset, length).ToArray();
            ecs[b] = ReedSolomon.Remainder(blocks[b], generator);
            offset += length;
        }

        var longest = Math.Max(group1Data, group2Data);
        var result = new List<byte>(data.Length + blockCount * ecPerBlock);
        for (var i = 0; i < longest; i++)
        {
            for (var b = 0; b < blockCount; b++)
            {
                if (i < blocks[b].Length)
                    result.Add(blocks[b][i]);
            }
        }

        for (var i = 0; i < ecPerBlock; i++)
        {
            for (var b = 0; b < blockCount; b++)
                result.Add(ecs[b][i]);
        }

        return [.. result];
    }

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, int version)
    {
        var size = modules.GetLength(0);

        // Timing patterns.
        for (var i = 0; i < size; i++)
        {
            Set(modules, isFunction, 6, i, i % 2 == 0);
            Set(modules, isFunction, i, 6, i % 2 == 0);
        }

        // Finder patterns with their separators.
        DrawFinder(modules, isFunction, 3, 3);
        DrawFinder(modules, isFunction, size - 4, 3);
        DrawFinder(modules, isFunction, 3, size - 4);

        // Alignment patterns, except where they would overlap a finder.
        var positions = QrTables.AlignmentPositions(version);
        for (var i = 0; i < positions.Length; i++)
        {
            for (var j = 0; j < positions.Length; j++)
            {
                if (i == 0 && j == 0 || i == 0 && j == positions.Length - 1 || i == positions.Length - 1 && j == 0)
                    continue;
                DrawAlignment(modules, isFunction, positions[i], positions[j]);
            }
        }

        // Reserved areas for the format information (drawn later) and the version information.
        DrawFormat(modules, 0);
        for (var i = 0; i < 8; i++)
        {
            isFunction[8, i] = isFunction[i, 8] = true;
            isFunction[8, size - 1 - i] = isFunction[size - 1 - i, 8] = true;
        }

        isFunction[8, 8] = true;
        if (version >= 7)
            DrawVersion(modules, isFunction, version);
    }

    private static void DrawFinder(bool[,] modules, bool[,] isFunction, int cx, int cy)
    {
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var x = cx + dx;
                var y = cy + dy;
                if (x < 0 || y < 0 || x >= modules.GetLength(0) || y >= modules.GetLength(0))
                    continue;
                var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                Set(modules, isFunction, x, y, distance != 2 && distance != 4);
            }
        }
    }

    private static void DrawAlignment(bool[,] modules, bool[,] isFunction, int cx, int cy)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
                Set(modules, isFunction, cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }
    }

    /// <summary>The 15-bit format information (level M, the mask) in both its places, BCH-protected.</summary>
    private static void DrawFormat(bool[,] modules, int mask)
    {
        var size = modules.GetLength(0);
        var data = (0b00 << 3) | mask; // level M is 00
        var rem = data;
        for (var i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = ((data << 10) | rem) ^ 0x5412;

        for (var i = 0; i <= 5; i++)
            modules[i, 8] = Bit(bits, i);
        modules[7, 8] = Bit(bits, 6);
        modules[8, 8] = Bit(bits, 7);
        modules[8, 7] = Bit(bits, 8);
        for (var i = 9; i < 15; i++)
            modules[8, 14 - i] = Bit(bits, i);

        for (var i = 0; i < 8; i++)
            modules[8, size - 1 - i] = Bit(bits, i);
        for (var i = 8; i < 15; i++)
            modules[size - 15 + i, 8] = Bit(bits, i);
        modules[size - 8, 8] = true; // always dark
    }

    private static void DrawVersion(bool[,] modules, bool[,] isFunction, int version)
    {
        var size = modules.GetLength(0);
        var rem = version;
        for (var i = 0; i < 12; i++)
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        var bits = (version << 12) | rem;
        for (var i = 0; i < 18; i++)
        {
            var a = size - 11 + i % 3;
            var b = i / 3;
            Set(modules, isFunction, a, b, Bit(bits, i));
            Set(modules, isFunction, b, a, Bit(bits, i));
        }
    }

    /// <summary>The zigzag placement: two columns at a time from the right, up then down, skipping the vertical timing column.</summary>
    private static void PlaceData(bool[,] modules, bool[,] isFunction, byte[] codewords, int version)
    {
        var size = modules.GetLength(0);
        var i = 0;
        var totalBits = codewords.Length * 8;
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
                right = 5;
            for (var vertical = 0; vertical < size; vertical++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? size - 1 - vertical : vertical;
                    if (isFunction[y, x] || i >= totalBits)
                        continue;
                    modules[y, x] = Bit(codewords[i >> 3], 7 - (i & 7));
                    i++;
                }
            }
        }
    }

    private static void ApplyMask(bool[,] modules, bool[,] isFunction, int mask)
    {
        var size = modules.GetLength(0);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (isFunction[y, x])
                    continue;
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                modules[y, x] ^= invert;
            }
        }
    }

    private static void Set(bool[,] modules, bool[,] isFunction, int x, int y, bool dark)
    {
        modules[y, x] = dark;
        isFunction[y, x] = true;
    }

    private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;
}
