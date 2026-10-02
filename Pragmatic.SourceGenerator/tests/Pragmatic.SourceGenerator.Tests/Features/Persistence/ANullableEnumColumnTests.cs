using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     An entity with a <b>nullable enum</b> property compiles: the bulk metadata casts it to
///     <c>int?</c> and not to <c>int</c>.
/// </summary>
/// <remarks>
///     The repository's <c>BulkMetadata.ReadValue</c> switch asked <c>prop.IsEnum</c> first and
///     emitted <c>(int)entity.Outcome</c>, which on a <c>Nullable&lt;TEnum&gt;</c> is <b>CS8629</b> — "a
///     nullable value type may be null". Measured on Casework's <c>Verification.Outcome</c>:
///     the module stopped compiling on a generated file, on the day an outcome became "not answered yet".
///     A nullable enum is an ordinary way to say "no value yet"; the first one in this repository broke
///     the build of the module that declared it.
/// </remarks>
public class ANullableEnumColumnTests
{
    private static string Model(string outcome) => $$"""
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace TestApp
        {
            [Boundary]
            public partial class VerifyBoundary { }

            public enum Outcome { Passed, Failed }

            [Entity]
            [BelongsTo<VerifyBoundary>]
            public partial class Verification : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public {{outcome}} Result { get; set; }
            }

            [PragmaticDbContext("Verify")]
            public partial class VerifyDbContext { }
        }
        """;

    [Fact]
    public void ANullableEnum_Compiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model("Outcome?"),
            static path => path.Contains("Verification") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    /// <summary>The control: a non-nullable enum keeps the cast it always had.</summary>
    [Fact]
    public void ANonNullableEnum_IsReadAsAnInt()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model("Outcome"));

        sources.First(s => s.Key.Contains("Verification.Repository")).Value
            .Should().Contain("\"Result\" => (int)entity.Result,",
                "an enum that cannot be null is read as the int it is stored as");
    }

    [Fact]
    public void ANullableEnum_IsReadAsANullableInt()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model("Outcome?"));

        sources.First(s => s.Key.Contains("Verification.Repository")).Value
            .Should().Contain("\"Result\" => (int?)entity.Result,",
                "and one that can is read as a nullable int — the column is nullable too");
    }
}
