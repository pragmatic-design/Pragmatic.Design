using System.Text;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Fuzz;

/// <summary>
///     The text parsing a route or a query parameter reaches (#311), through <see cref="IParsable{TSelf}" /> as the
///     binder calls it, for every type on the same text.
/// </summary>
/// <remarks>
///     The declared outcomes are <c>TryParse</c> answering true or false and <c>Parse</c> returning or throwing
///     <see cref="FormatException" />. Those return; anything else escapes and is a crash, and so is the two
///     disagreeing on the same text (<see cref="FuzzFinding" />).
/// </remarks>
public static class TemporalParseTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);

        Parse<LocalDate>(text);
        Parse<LocalTime>(text);
        Parse<LocalDateTime>(text);
        Parse<Duration>(text);
        Parse<Period>(text);
    }

    private static void Parse<T>(string text) where T : IParsable<T>
    {
        var parsed = T.TryParse(text, null, out _);
        try
        {
            T.Parse(text, null);
            if (!parsed)
                throw new FuzzFinding($"{typeof(T).Name}: Parse accepted what TryParse refused.");
        }
        catch (FormatException) when (!parsed)
        {
        }
    }
}
