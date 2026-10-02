using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     PRAG0651 asks for a value converter, and must not ask for one the generator already wrote.
/// </summary>
/// <remarks>
///     <para>
///         The check is a name-based allow-list of primitives: anything else is assumed unmappable.
///         Two kinds of property fail that test and are nonetheless mapped — by the entity
///         configuration this same generator emits a few files away. A <c>[ValueObject]</c> becomes
///         <c>builder.ComplexProperty(...)</c>, and a collection of primitives becomes
///         <c>builder.Property(...)</c> as an EF Core primitive collection.
///     </para>
///     <para>
///         Warning about those points the author at the generator's own output and asks them to fix
///         it. Found by declaring both in an application and reading the build log.
///     </para>
/// </remarks>
public class ValueConverterDiagnosticTests
{
    [Fact]
    public void AValueObjectProperty_IsNotAskedForAConverter()
    {
        var result = Run("""
            [ValueObject]
            public partial record TextSpan
            {
                public TextSpan(int start, int length) { Start = start; Length = length; }
                public int Start { get; init; }
                public int Length { get; init; }
                private static TextSpan Validate(int start, int length) => new(start, length);
            }

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Mention : IEntity
            {
                public TextSpan Position { get; private set; } = new(0, 0);
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0651").Should().BeFalse(
            "the generator maps it as an EF complex type itself, so asking for a converter sends the "
            + "author to fix generated code that is already correct");
    }

    [Fact]
    public void APrimitiveCollection_IsNotAskedForAConverter()
    {
        var result = Run("""
            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Term : IEntity
            {
                public List<string> Aliases { get; private set; } = [];
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0651").Should().BeFalse(
            "EF Core stores a collection of primitives as a JSON column, and the generated "
            + "configuration already says so");
    }

    /// <summary>
    ///     The control: a type nothing knows how to map still gets the warning.
    /// </summary>
    /// <remarks>
    ///     Without this, the two tests above would also pass on a diagnostic that had been deleted
    ///     rather than narrowed.
    /// </remarks>
    [Fact]
    public void AnUnmappableType_IsStillAskedForAConverter()
    {
        var result = Run("""
            public sealed class Whatever { public string Value { get; set; } = ""; }

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Odd : IEntity
            {
                public Whatever Thing { get; private set; } = new();
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0651").Should().BeTrue(
            "nothing maps a plain class, and that is the case the diagnostic exists for");
    }

    private static SourceGenRunResult Run(string declarations)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Persistence.Entity;

            namespace Converters;

            public sealed class ThingBoundary;

            {{declarations}}
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Mapping.Attributes.MapFromAttribute<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)));
}
