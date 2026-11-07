using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A derived entity's generated <c>Create</c> overloads that have its base entity's parameters are declared <c>new</c> (#540), and only those:
///     without it a hierarchy whose root is not <c>[Auditable]</c> is CS0108 (the root's <c>Create()</c> and the derived type's), an error under
///     <c>--warnaserror</c>; with it where the signatures differ, CS0109.
/// </summary>
public class ADerivedEntitysCreateHidesItsBasesTests
{
    private const string Hierarchy = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Entities {

        [Entity]
        [Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "ExtraType")]
        public partial class Extra : IEntity
        {
            public string? Note { get; set; }
        }

        /// <summary>Create(int bottles) and Create(): the second has the root's signature.</summary>
        [Entity]
        public partial class MinibarExtra : Extra
        {
            public int Bottles { get; private set; }
        }

        /// <summary>Create() only: the root's signature.</summary>
        [Entity]
        public partial class LateArrivalExtra : Extra
        {
            public string? Gate { get; set; }
        }

        /// <summary>An auditable root: Create(string, TimeProvider?) and Create(TimeProvider?).</summary>
        [Entity]
        [Auditable]
        [Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "ChargeType")]
        public partial class Charge : IEntity
        {
            public string Reason { get; private set; }
        }

        /// <summary>Not auditable itself: Create(int) and Create(), neither of them the root's.</summary>
        [Entity]
        public partial class LateCharge : Charge
        {
            public int HoursLate { get; private set; }
        }

        }
        """;

    /// <summary>The Sql engine's bridge, by the type the generator recognises it by.</summary>
    private const string SqlBridge = "namespace Pragmatic.Persistence.Sql { public static class PragmaticSqlFilters { } }";

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Entity.EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        .. new[] { "Pragmatic.Abstractions", "Pragmatic.Identity", "Pragmatic.Authorization", "Pragmatic.Specification", "System.Linq.Queryable", "System.Text.Json",
                "System.ComponentModel.Annotations" }
            .Select(n => GeneratorTestHelper.TryGetAssemblyReference(n) ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process.")),
    ];

    /// <summary>The hierarchy on EF Core or on the Sql engine: the factory is the entity's shape, the same on both.</summary>
    private static SourceGenRunResult Run(string engine)
        => engine == "EfCore"
            ? GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Hierarchy,
                [.. References, GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()])
            : GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Hierarchy + "\n" + SqlBridge, References);

    private static string Create(SourceGenRunResult result, string type)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)[$"Probe.Entities.{type}.Create.g.cs"];

    /// <summary>The setpoint: no factory hides another without saying so, and none says so where it hides nothing.</summary>
    [Theory]
    [InlineData("EfCore")]
    [InlineData("Sql")]
    public void TheHierarchy_CompilesWithoutHidingWarnings(string engine)
    {
        var result = Run(engine);
        var diagnostics = result.OutputCompilation.GetDiagnostics();

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys.Should().Contain("Probe.Entities.MinibarExtra.Create.g.cs", "the control: the factories are there");
        diagnostics.Where(d => d.Id is "CS0108" or "CS0109").Select(d => d.ToString()).Should().BeEmpty();
    }

    /// <summary>The overloads with the root's signature are the ones declared <c>new</c>; the one with a parameter of its own is not.</summary>
    [Theory]
    [InlineData("EfCore")]
    [InlineData("Sql")]
    public void OnlyTheOverloadsWithTheBasesSignature_AreDeclaredNew(string engine)
    {
        var result = Run(engine);

        Create(result, "MinibarExtra").Should().Contain("public new static global::Probe.Entities.MinibarExtra Create()")
            .And.Contain("public static global::Probe.Entities.MinibarExtra Create(int bottles)");
        Create(result, "LateArrivalExtra").Should().Contain("public new static global::Probe.Entities.LateArrivalExtra Create()");
        Create(result, "Extra").Should().NotContain(" new static", "the root hides nothing");
        Create(result, "LateCharge").Should().NotContain(" new static", "an auditable root's factories take a TimeProvider, the derived type's do not");
    }
}
