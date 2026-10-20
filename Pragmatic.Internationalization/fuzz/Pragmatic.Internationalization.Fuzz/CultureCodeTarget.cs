using System.Text;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Fuzz;

/// <summary>
///     The culture a request names, in its query string or its <c>Accept-Language</c> header, read on every request
///     the I18n middleware sees (#312).
/// </summary>
/// <remarks>
///     The declared outcomes are <see cref="CultureCode.TryFromString" /> answering true or false,
///     <see cref="CultureCode.FromString" /> agreeing with it or refusing with <see cref="ArgumentException" />, and an
///     accepted culture reading back as itself. Anything else is a crash (<see cref="FuzzFinding" /> for a
///     disagreement).
/// </remarks>
public static class CultureCodeTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);

        var parsed = CultureCode.TryFromString(text, out var culture);
        try
        {
            if (CultureCode.FromString(text) != culture || !parsed)
                throw new FuzzFinding("FromString accepted what TryFromString refused, or read it differently.");
        }
        catch (ArgumentException) when (!parsed)
        {
        }

        if (parsed && (!CultureCode.TryFromString(culture.Code, out var again) || again != culture))
            throw new FuzzFinding($"'{culture.Code}' does not read back as itself.");

        if (CultureCode.FromAcceptLanguage(text) is { } preferred && !CultureCode.IsValid(preferred.Code))
            throw new FuzzFinding($"The header gave '{preferred.Code}', which is not a culture.");
    }
}
