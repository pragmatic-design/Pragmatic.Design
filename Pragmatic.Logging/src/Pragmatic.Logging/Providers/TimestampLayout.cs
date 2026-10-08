namespace Pragmatic.Logging.Providers;

/// <summary>
///     A timestamp format read once, written digit by digit: the text <c>DateTime.TryFormat</c> writes for the
///     same format under the invariant culture, without reading the format again on every line.
/// </summary>
/// <remarks>
///     <para>
///         Parsing the custom format was most of the cost of a JSON line from a generated call site: about
///         90 ns of 366 (the subtraction table in <c>Pragmatic.Logging/BENCHMARK-RESULTS.md</c>).
///     </para>
///     <para>
///         Only zero-padded numbers and separators are read — <c>yyyy MM dd HH mm ss</c>, one to seven
///         <c>f</c>, and <c>- : . , / space T Z</c>, which the invariant culture writes as themselves. Anything
///         else (names, offsets, quotes, escapes, 12-hour clocks, unpadded numbers, standard formats) makes
///         <see cref="Parse" /> return <see langword="null" />, and the caller formats through
///         <see cref="DateTime" /> as before.
///     </para>
/// </remarks>
internal sealed class TimestampLayout
{
    private const string Literals = "-:.,/ TZ";

    // The JSON provider's default, written in a straight line rather than through the loop below: 16 ns,
    // where Utf8Formatter's "O" takes 11 (TimestampBenchmarks).
    private const string IsoMilliseconds = "yyyy-MM-ddTHH:mm:ss.fffZ";

    // Part i is _specifiers[i] repeated _widths[i] times: a field letter, or a separator written as itself.
    private readonly char[] _specifiers;
    private readonly int[] _widths;
    private readonly int _length;
    private readonly bool _isoMilliseconds;

    private TimestampLayout(char[] specifiers, int[] widths, int length, bool isoMilliseconds)
    {
        _specifiers = specifiers;
        _widths = widths;
        _length = length;
        _isoMilliseconds = isoMilliseconds;
    }

    /// <summary>The layout of <paramref name="format" />, or <see langword="null" /> when it is not one this type reads.</summary>
    public static TimestampLayout? Parse(string format)
    {
        if (string.IsNullOrEmpty(format))
            return null;

        var specifiers = new List<char>();
        var widths = new List<int>();
        for (var i = 0; i < format.Length;)
        {
            var c = format[i];
            var run = 1;
            while (i + run < format.Length && format[i + run] == c)
                run++;

            var read = c switch
            {
                'y' => run == 4,
                'M' or 'd' or 'H' or 'm' or 's' => run == 2,
                'f' => run <= 7,
                _ => Literals.Contains(c),
            };

            if (!read)
                return null;

            specifiers.Add(c);
            widths.Add(run);
            i += run;
        }

        return new TimestampLayout([.. specifiers], [.. widths], format.Length, format == IsoMilliseconds);
    }

    /// <summary>Writes <paramref name="timestamp" /> in this layout as UTF-8.</summary>
    public bool TryFormat(DateTime timestamp, Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < _length)
        {
            bytesWritten = 0;
            return false;
        }

        // Every field from one split of the ticks: DateTime's Hour, Minute and Second each divide the ticks again.
        var (year, month, day) = timestamp;
        var timeOfDay = timestamp.Ticks % TimeSpan.TicksPerDay;
        var seconds = (int)(timeOfDay / TimeSpan.TicksPerSecond);
        var fraction = (int)(timeOfDay % TimeSpan.TicksPerSecond);

        if (_isoMilliseconds)
        {
            Four(destination, 0, year);
            destination[4] = (byte)'-';
            Two(destination, 5, month);
            destination[7] = (byte)'-';
            Two(destination, 8, day);
            destination[10] = (byte)'T';
            Two(destination, 11, seconds / 3600);
            destination[13] = (byte)':';
            Two(destination, 14, seconds / 60 % 60);
            destination[16] = (byte)':';
            Two(destination, 17, seconds % 60);
            destination[19] = (byte)'.';
            var milliseconds = fraction / 10_000;
            destination[20] = (byte)('0' + milliseconds / 100);
            Two(destination, 21, milliseconds % 100);
            destination[23] = (byte)'Z';
            bytesWritten = 24;
            return true;
        }

        var position = 0;
        for (var i = 0; i < _specifiers.Length; i++)
        {
            var width = _widths[i];
            switch (_specifiers[i])
            {
                case 'y': Four(destination, position, year); break;
                case 'M': Two(destination, position, month); break;
                case 'd': Two(destination, position, day); break;
                case 'H': Two(destination, position, seconds / 3600); break;
                case 'm': Two(destination, position, seconds / 60 % 60); break;
                case 's': Two(destination, position, seconds % 60); break;
                case 'f': Fraction(destination.Slice(position, width), fraction); break;
                default: destination.Slice(position, width).Fill((byte)_specifiers[i]); break;
            }

            position += width;
        }

        bytesWritten = _length;
        return true;
    }

    private static void Two(Span<byte> destination, int position, int value)
    {
        destination[position] = (byte)('0' + value / 10);
        destination[position + 1] = (byte)('0' + value % 10);
    }

    private static void Four(Span<byte> destination, int position, int value)
    {
        Two(destination, position, value / 100);
        Two(destination, position + 2, value % 100);
    }

    // The leading digits of the seven-digit fraction, truncated as the "f" specifiers truncate.
    private static void Fraction(Span<byte> destination, int fraction)
    {
        var divisor = 1_000_000;
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)('0' + fraction / divisor % 10);
            divisor /= 10;
        }
    }
}
