using System;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Identity;

/// <summary>
///     A role names a constant this very run generates, and the catalogue has to say what it grants.
/// </summary>
/// <remarks>
///     <para>
///         An entity's CRUD permission constants are written by the persistence pipeline in the same
///         compilation, so during the role transform they are not symbols and <c>GetConstantValue</c>
///         has nothing to fold. Dropped where they are read, a role granting four entity permissions
///         would come out of the catalogue granting only the one that is hand-written, while the
///         runtime — which reads the property — grants all four.
///     </para>
///     <para>
///         ⚠️ Nothing would fail. What is applied stays right and only the catalogue lies, so a screen
///         listing a role's grants states a falsehood. The transform declares the name it cannot bind
///         and the aggregation step answers it from the permission
///         catalogue — the level that has it, and the shape every other cross-feature fact in this
///         generator uses.
///     </para>
///     <para>
///         Here rather than in <c>Pragmatic.Authorization.Tests</c> because the case needs Persistence
///         and Actions to bind: that suite references neither, so the entity would not be an entity
///         and no constant would be generated to name.
///     </para>
/// </remarks>
public class ARoleGrantingAGeneratedConstantTests
{
    private const string Source = """
        using System.Collections.Generic;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Authorization;
        using Pragmatic.Persistence.Entity;

        namespace TestApp.Knowledge;

        [Boundary]
        public partial class KnowledgeBoundary;

        [Entity]
        [BelongsTo<KnowledgeBoundary>]
        public partial class Glossary : IEntity
        {
            public string Word { get; private set; } = "";
        }

        public sealed class Steward : IRole
        {
            public static string Name => "steward";
            public static string? Description => "Curates the knowledge base";
            public static IReadOnlyList<string> DefaultPermissions =>
                [KnowledgePermissions.Glossary.Read, KnowledgePermissions.Glossary.Update];
        }
        """;

    /// <summary>The catalogue lists what the role actually grants.</summary>
    [Fact]
    public void TheGeneratedConstantsARoleNames_ReachTheCatalogue()
    {
        var registry = Registry();

        registry.Should().Contain("glossary.read",
            "the constant names a permission this run generates, and the catalogue holds its value");
        registry.Should().Contain("glossary.update");
    }

    /// <summary>
    ///     The control: a name that matches nothing in the catalogue is not invented.
    /// </summary>
    /// <remarks>
    ///     Without it, "the names reach the catalogue" would be satisfied by carrying the path itself
    ///     into the grant — a catalogue naming a permission nobody can ever be granted, which is the
    ///     defect this fix exists to remove, spelled differently.
    /// </remarks>
    [Fact]
    public void ANameTheCatalogueDoesNotHold_IsNotInvented()
    {
        var registry = Registry();

        registry.Should().NotContain("KnowledgePermissions.Glossary.Read",
            "the const path is not a permission value");
    }

    private static string Registry()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source,
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Entity.EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
            // FromTypeAssembly, not FromType: IRole has static abstract members, so it cannot be a
            // type argument (CS8920). The reference is the assembly, not the type.
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Authorization.IRole)),
            // The flag FeatureDetector looks for to turn the Identity feature on. Without it the
            // registry is not generated at all and every assertion below would be about nothing.
            GeneratorTestHelper.FromTypeAssembly(
                typeof(global::Pragmatic.Authorization.PragmaticBuilderAuthorizationExtensions)),
            GeneratorTestHelper.FromType<global::Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
            GeneratorTestHelper.FromTypeAssembly(
                typeof(global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
            GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
            GeneratorTestHelper.TryGetAssemblyReference("System.Text.Json")
                ?? throw new InvalidOperationException("System.Text.Json does not resolve"),
            GeneratorTestHelper.TryGetAssemblyReference("System.ComponentModel.TypeConverter")
                ?? throw new InvalidOperationException("TypeConverter does not resolve"));

        var registry = GeneratorTestHelper.GetGeneratedSource(result, "PermissionRegistry");

        registry.Should().NotBeNull("the role produces a registry at all");

        return registry!;
    }
}
