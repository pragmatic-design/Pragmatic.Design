using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Date conversions honour the nullable flag like every other conversion.
/// </summary>
/// <remarks>
///     <para>
///         Ignoring the flag would emit <c>DateOnly.FromDateTime(this.X)</c> with a <c>DateTime?</c>
///         source too, that is <c>CS1503</c> inside a generated file. No date conversion has a reason to
///         behave differently from a numeric one.
///     </para>
///     <para>
///         ⚠️ The control case is the <b>non</b>-nullable source: without it, a guard applied always —
///         the opposite error, equally wrong, because it would wrap in a ternary a value that cannot be
///         absent — would go unnoticed.
///     </para>
/// </remarks>
public class NullableDateConversionTests : MappingGeneratorTestBase
{
    private static string Source(string sourceType) => $$"""
        using System;
        using Pragmatic.Mapping.Attributes;

        public class Src { public {{sourceType}} Moment { get; set; } }

        [MapFrom<Src>]
        public partial class Dto { public DateOnly Moment { get; init; } }
        """;

    [Fact]
    public void ANullableDateTime_IsUnwrappedBeforeTheConversion()
    {
        var result = RunGenerator(Source("DateTime?"));

        var mapping = GetGeneratedSource(result, "Dto.Mapping");
        mapping.Should().NotBeNull();
        mapping!.Should().Contain("is { } __v",
            "a source that can be missing is unwrapped before converting it");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
    }

    [Fact]
    public void TheControl_ANonNullableDateTime_IsConvertedDirectly()
    {
        var result = RunGenerator(Source("DateTime"));

        var mapping = GetGeneratedSource(result, "Dto.Mapping");
        mapping.Should().NotBeNull();
        mapping!.Should().Contain("DateOnly.FromDateTime(");
        mapping.Should().NotContain("is { } __v",
            "a value that cannot be missing needs no guard");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
    }
}
