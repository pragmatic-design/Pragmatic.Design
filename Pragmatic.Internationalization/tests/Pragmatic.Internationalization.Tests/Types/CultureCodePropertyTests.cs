using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Types;

/// <summary>
///     <see cref="CultureCode.TryFromString" /> on the culture a request names, in its query string or its
///     <c>Accept-Language</c> header, on every request the I18n middleware sees (#158, #312). For any text it answers
///     true or false; <see cref="CultureCode.FromString" /> agrees with it and refuses with
///     <see cref="ArgumentException" />; what it accepts reads back as itself.
/// </summary>
public class CultureCodePropertyTests
{
    // Codes that exist in either case, codes that do not, the separator a culture code splits on and the ones it does
    // not, whitespace, and letters outside ASCII whose case mapping differs by culture (the Turkish dotted I).
    private static readonly string[] Pieces =
        ["en", "it", "IT", "us", "US", "de", "CH", "zh", "Hans", "xx", "e", "-", "--", "_", ";", ",", " ", "\t", "İ", "ı", "é", "😀"];

    // Null too, which the parameter admits and FsCheck's string arbitrary does not produce.
    private static readonly Arbitrary<string?> Texts =
        Gen.Frequency(
                (3, Gen.Elements(Pieces).ListOf().Select(pieces => (string?)string.Concat(pieces))),
                (1, ArbMap.Default.ArbFor<string>().Generator.Select(text => (string?)text)),
                (1, Gen.Constant<string?>(null)))
            .ToArbitrary();

    [Fact]
    public void Any_text_is_a_culture_or_refused()
        => Prop.ForAll(Texts, text =>
        {
            var parsed = CultureCode.TryFromString(text, out var culture);
            try
            {
                var created = CultureCode.FromString(text!);
                return parsed && created == culture;
            }
            catch (ArgumentException)
            {
                return !parsed;
            }
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(2000));

    [Fact]
    public void An_accepted_culture_reads_back_as_itself()
        => Prop.ForAll(Texts, text =>
            !CultureCode.TryFromString(text, out var culture)
            || (CultureCode.TryFromString(culture.Code, out var again) && again == culture))
            .Check(Config.QuickThrowOnFailure.WithMaxTest(2000));

    [Fact]
    public void Any_accept_language_header_gives_a_culture_or_none()
        => Prop.ForAll(Texts, header =>
        {
            var culture = CultureCode.FromAcceptLanguage(header);
            return culture is null || CultureCode.IsValid(culture.Value.Code);
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(2000));
}
