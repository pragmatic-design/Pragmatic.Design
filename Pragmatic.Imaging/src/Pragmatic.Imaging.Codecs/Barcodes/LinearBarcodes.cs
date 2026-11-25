using System.Globalization;

namespace Pragmatic.Imaging.Codecs.Barcodes;

/// <summary>
///     One-dimensional barcodes as runs of bar and space widths, in modules, starting with a bar: Code
///     128 (sets B and C, with the check character), Code 39 (with start and stop asterisks) and EAN-13
///     and EAN-8 (with the check digit computed or verified).
/// </summary>
public static class LinearBarcodes
{
    /// <summary>The element widths of Code 128 symbols 0..106, three bars and three spaces each (the stop symbol has a fourth bar).</summary>
    internal static readonly string[] Code128Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    private const int Code128StartB = 104;
    private const int Code128StartC = 105;
    private const int Code128CodeB = 100;
    private const int Code128CodeC = 99;
    private const int Code128Stop = 106;

    /// <summary>Code 39: the character and its nine elements, 'n' narrow and 'w' wide, bars and spaces alternating from a bar.</summary>
    private static readonly (char Character, string Elements)[] Code39Patterns =
    [
        ('0', "nnnwwnwnn"), ('1', "wnnwnnnnw"), ('2', "nnwwnnnnw"), ('3', "wnwwnnnnn"), ('4', "nnnwwnnnw"),
        ('5', "wnnwwnnnn"), ('6', "nnwwwnnnn"), ('7', "nnnwnnwnw"), ('8', "wnnwnnwnn"), ('9', "nnwwnnwnn"),
        ('A', "wnnnnwnnw"), ('B', "nnwnnwnnw"), ('C', "wnwnnwnnn"), ('D', "nnnnwwnnw"), ('E', "wnnnwwnnn"),
        ('F', "nnwnwwnnn"), ('G', "nnnnnwwnw"), ('H', "wnnnnwwnn"), ('I', "nnwnnwwnn"), ('J', "nnnnwwwnn"),
        ('K', "wnnnnnnww"), ('L', "nnwnnnnww"), ('M', "wnwnnnnwn"), ('N', "nnnnwnnww"), ('O', "wnnnwnnwn"),
        ('P', "nnwnwnnwn"), ('Q', "nnnnnnwww"), ('R', "wnnnnnwwn"), ('S', "nnwnnnwwn"), ('T', "nnnnwnwwn"),
        ('U', "wwnnnnnnw"), ('V', "nwwnnnnnw"), ('W', "wwwnnnnnn"), ('X', "nwnnwnnnw"), ('Y', "wwnnwnnnn"),
        ('Z', "nwwnwnnnn"), ('-', "nwnnnnwnw"), ('.', "wwnnnnwnn"), (' ', "nwwnnnwnn"), ('$', "nwnwnwnnn"),
        ('/', "nwnwnnnwn"), ('+', "nwnnnwnwn"), ('%', "nnnwnwnwn"), ('*', "nwnnwnwnn"),
    ];

    /// <summary>EAN digit patterns, set L: the widths of space, bar, space, bar (set G is L reversed; set R has the same widths as L, bars and spaces swapped).</summary>
    private static readonly string[] EanL = ["3211", "2221", "2122", "1411", "1132", "1231", "1114", "1312", "1213", "3112"];

    /// <summary>Which set (L or G) each of the six left digits of an EAN-13 takes, by the first digit.</summary>
    private static readonly string[] Ean13Parity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    /// <summary>
    ///     Code 128 for printable ASCII: set C for the whole string when it is all digits of even length,
    ///     set B otherwise, with digit runs of four or more switched to set C. Widths alternate bar, space, bar…
    /// </summary>
    public static int[] Code128(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        foreach (var c in text)
        {
            if (c is < ' ' or > '~')
                throw new ArgumentException($"Code 128 here takes printable ASCII only; '{c}' is not.", nameof(text));
        }

        var symbols = new List<int>();
        var i = 0;
        var inC = text.Length >= 2 && text.Length % 2 == 0 && text.All(char.IsAsciiDigit);
        symbols.Add(inC ? Code128StartC : Code128StartB);
        while (i < text.Length)
        {
            var digitRun = 0;
            while (i + digitRun < text.Length && char.IsAsciiDigit(text[i + digitRun]))
                digitRun++;
            if (digitRun >= 4 || inC && digitRun >= 2)
            {
                if (!inC)
                {
                    symbols.Add(Code128CodeC);
                    inC = true;
                }

                var pairs = digitRun / 2;
                for (var p = 0; p < pairs; p++, i += 2)
                    symbols.Add(int.Parse(text.AsSpan(i, 2), CultureInfo.InvariantCulture));
                continue;
            }

            if (inC)
            {
                symbols.Add(Code128CodeB);
                inC = false;
            }

            symbols.Add(text[i] - ' ');
            i++;
        }

        var check = symbols[0];
        for (var s = 1; s < symbols.Count; s++)
            check += symbols[s] * s;
        symbols.Add(check % 103);
        symbols.Add(Code128Stop);

        var widths = new List<int>();
        foreach (var symbol in symbols)
        {
            foreach (var w in Code128Patterns[symbol])
                widths.Add(w - '0');
        }

        return [.. widths];
    }

    /// <summary>Code 39, narrow 1 and wide 3 modules, an inter-character gap of one, framed by asterisks. Lower-case letters are upper-cased.</summary>
    public static int[] Code39(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var widths = new List<int>();
        void Append(char c)
        {
            var index = Array.FindIndex(Code39Patterns, e => e.Character == c);
            if (index < 0)
                throw new ArgumentException($"Code 39 cannot encode '{c}'.", nameof(text));
            foreach (var e in Code39Patterns[index].Elements)
                widths.Add(e == 'w' ? 3 : 1);
            widths.Add(1); // the gap, a space
        }

        Append('*');
        foreach (var c in text.ToUpperInvariant())
        {
            if (c == '*')
                throw new ArgumentException("Code 39 cannot encode '*'.", nameof(text));
            Append(c);
        }

        Append('*');
        widths.RemoveAt(widths.Count - 1); // no gap after the stop
        return [.. widths];
    }

    /// <summary>EAN-13 from 12 or 13 digits (the check digit computed, or verified when given).</summary>
    public static int[] Ean13(string digits)
    {
        var text = WithCheckDigit(digits, 13);
        var widths = new List<int> { 1, 1, 1 }; // start guard: bar space bar
        var parity = Ean13Parity[text[0] - '0'];
        for (var i = 1; i <= 6; i++)
            AppendEanDigit(widths, text[i] - '0', parity[i - 1]);
        widths.AddRange([1, 1, 1, 1, 1]); // centre guard: space bar space bar space
        for (var i = 7; i <= 12; i++)
            AppendEanDigit(widths, text[i] - '0', 'R');
        widths.AddRange([1, 1, 1]); // end guard
        return [.. widths];
    }

    /// <summary>EAN-8 from 7 or 8 digits.</summary>
    public static int[] Ean8(string digits)
    {
        var text = WithCheckDigit(digits, 8);
        var widths = new List<int> { 1, 1, 1 };
        for (var i = 0; i < 4; i++)
            AppendEanDigit(widths, text[i] - '0', 'L');
        widths.AddRange([1, 1, 1, 1, 1]);
        for (var i = 4; i < 8; i++)
            AppendEanDigit(widths, text[i] - '0', 'R');
        widths.AddRange([1, 1, 1]);
        return [.. widths];
    }

    private static void AppendEanDigit(List<int> widths, int digit, char set)
    {
        // L and G patterns start with a space, R with a bar; the widths list alternates from the start guard's
        // last bar, so each digit's four elements continue the alternation as the sets define it.
        var pattern = EanL[digit];
        if (set == 'G')
        {
            for (var i = 3; i >= 0; i--)
                widths.Add(pattern[i] - '0');
        }
        else
        {
            foreach (var w in pattern)
                widths.Add(w - '0');
        }
    }

    private static string WithCheckDigit(string digits, int length)
    {
        ArgumentException.ThrowIfNullOrEmpty(digits);
        if (digits.Length != length && digits.Length != length - 1 || !digits.All(char.IsAsciiDigit))
            throw new ArgumentException($"An EAN-{length} needs {length - 1} or {length} digits.", nameof(digits));
        var sum = 0;
        for (var i = 0; i < length - 1; i++)
        {
            var weight = (length - 1 - i) % 2 == 1 ? 3 : 1;
            sum += (digits[i] - '0') * weight;
        }

        var check = (10 - sum % 10) % 10;
        if (digits.Length == length && digits[^1] - '0' != check)
            throw new ArgumentException($"The EAN-{length} check digit should be {check}.", nameof(digits));
        return digits.Length == length ? digits : digits + (char)('0' + check);
    }
}
