using System;
using System.Linq;
using System.Text.Json;
using Pragmatic.Internationalization.AspNetCore.Json.Extensions;
using Pragmatic.SourceGenerator.Features.Manifest;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     The types the generator watches for on the wire are the ones the extension installs converters
///     for — checked against the extension itself, not against a memory of it.
/// </summary>
/// <remarks>
///     <para>
///         The generator targets netstandard2.0 and cannot reference
///         <c>Pragmatic.Internationalization</c>, so the list is names. A list of names inside a
///         generator ages in silence: a converter added to the extension and not here means an
///         application whose request carries that type gets a 400 before any rule runs, and nothing in
///         this repository fails.
///     </para>
///     <para>
///         ⚠️ Which is what the dependency validator's list had to undo, one range over. The cure there was to move
///         the list to a declaration; here there is nothing to declare on — the types are somebody
///         else's, and the question is which converters one call adds. So the list stays, and this test
///         is what keeps it honest.
///     </para>
/// </remarks>
public class TheConvertersAnAmountNeedsTests
{
    /// <summary>The types the converters that call installs actually handle.</summary>
    private static readonly string[] Installed = Handled();

    [Fact]
    public void EveryTypeTheExtensionConverts_IsOneTheGeneratorWatchesFor()
        => Installed.Except(I18nWireTypes.Names).Should().BeEmpty(
            "a converter the extension installs and the generator does not watch for is an application "
            + "that answers 400 to a request carrying that type, with nothing here going red");

    [Fact]
    public void EveryTypeTheGeneratorWatchesFor_IsOneTheExtensionConverts()
        => I18nWireTypes.Names.Except(Installed).Should().BeEmpty(
            "a name that matches no converter makes the host install i18n for a shape that never "
            + "needed it — the presence wiring this declaration exists to avoid");

    /// <summary>
    ///     The types handled by the converters <c>AddPragmaticInternationalization</c> adds, with the
    ///     nullable pairs folded onto the type they wrap.
    /// </summary>
    private static string[] Handled()
    {
        var options = new JsonSerializerOptions();
        options.AddPragmaticInternationalization();

        return
        [
            .. options.Converters
                .Select(converter => converter.Type)
                .Where(type => type is not null)
                .Select(type => Nullable.GetUnderlyingType(type!) ?? type!)
                .Select(type => type.FullName!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
        ];
    }
}
