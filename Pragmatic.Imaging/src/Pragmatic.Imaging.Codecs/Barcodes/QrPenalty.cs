namespace Pragmatic.Imaging.Codecs.Barcodes;

/// <summary>The four mask penalty rules of ISO/IEC 18004, scored as the reference implementations score them.</summary>
internal static class QrPenalty
{
    public static int Score(bool[,] m)
    {
        var size = m.GetLength(0);
        var penalty = 0;
        // One run history for every row and column, cleared for each: scored eight times per symbol,
        // an array per line was most of what encoding a QR code allocated.
        Span<int> history = stackalloc int[7];

        // Rule 1: runs of five or more same-coloured modules in a row or column. Rule 3: finder-like patterns.
        for (var y = 0; y < size; y++)
        {
            var runColor = false;
            var runX = 0;
            history.Clear();
            for (var x = 0; x < size; x++)
            {
                if (m[y, x] == runColor)
                {
                    runX++;
                    if (runX == 5)
                        penalty += 3;
                    else if (runX > 5)
                        penalty++;
                }
                else
                {
                    FinderPenaltyAddHistory(history, runX, size);
                    if (!runColor)
                        penalty += FinderPenaltyCount(history) * 40;
                    runColor = m[y, x];
                    runX = 1;
                }
            }

            penalty += FinderPenaltyTerminate(history, runColor, runX, size) * 40;
        }

        for (var x = 0; x < size; x++)
        {
            var runColor = false;
            var runY = 0;
            history.Clear();
            for (var y = 0; y < size; y++)
            {
                if (m[y, x] == runColor)
                {
                    runY++;
                    if (runY == 5)
                        penalty += 3;
                    else if (runY > 5)
                        penalty++;
                }
                else
                {
                    FinderPenaltyAddHistory(history, runY, size);
                    if (!runColor)
                        penalty += FinderPenaltyCount(history) * 40;
                    runColor = m[y, x];
                    runY = 1;
                }
            }

            penalty += FinderPenaltyTerminate(history, runColor, runY, size) * 40;
        }

        // Rule 2: 2×2 blocks of one colour.
        for (var y = 0; y < size - 1; y++)
        {
            for (var x = 0; x < size - 1; x++)
            {
                var c = m[y, x];
                if (c == m[y, x + 1] && c == m[y + 1, x] && c == m[y + 1, x + 1])
                    penalty += 3;
            }
        }

        // Rule 4: the balance of dark modules.
        var dark = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
                dark += m[y, x] ? 1 : 0;
        }

        var total = size * size;
        var k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        penalty += k * 10;
        return penalty;
    }

    private static void FinderPenaltyAddHistory(Span<int> history, int runLength, int size)
    {
        if (history[0] == 0)
            runLength += size; // the light border counts as a run before the first module
        history[..6].CopyTo(history[1..]);
        history[0] = runLength;
    }

    private static int FinderPenaltyCount(ReadOnlySpan<int> h)
    {
        var n = h[1];
        var core = n > 0 && h[2] == n && h[3] == n * 3 && h[4] == n && h[5] == n;
        return (core && h[0] >= n * 4 && h[6] >= n ? 1 : 0) + (core && h[6] >= n * 4 && h[0] >= n ? 1 : 0);
    }

    private static int FinderPenaltyTerminate(Span<int> history, bool currentColor, int runLength, int size)
    {
        if (currentColor)
        {
            FinderPenaltyAddHistory(history, runLength, size);
            runLength = 0;
        }

        runLength += size;
        FinderPenaltyAddHistory(history, runLength, size);
        return FinderPenaltyCount(history);
    }
}
