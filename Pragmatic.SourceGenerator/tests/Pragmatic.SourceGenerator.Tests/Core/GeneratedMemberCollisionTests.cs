using System;
using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Two mechanisms that would generate a member with the same name on one entity.
/// </summary>
/// <remarks>
///     <para>
///         <c>TraitPropertyResolver</c> answers "what will exist on this entity once generation has
///         run" by concatenating four sources: traits, relation foreign keys, relation navigations
///         and the inverse ones. It concatenates — it does not compare.
///     </para>
///     <para>
///         ⚠️ Measured before building anything. The specification proposed "a diagnostic for members
///         declared twice, because today the first to answer wins, in silence", and the first thing
///         to establish is whether the collision is reachable or only imaginable.
///     </para>
/// </remarks>
public class GeneratedMemberCollisionTests
{
    private const string Preamble = """
        using System;
        using Pragmatic.Persistence.Entity;

        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Person : IEntity
        {
            public string Name { get; set; } = "";
        }
        """;

    /// <summary>
    ///     Two relations to the same target, neither naming its navigation.
    /// </summary>
    /// <remarks>
    ///     Both derive the same foreign key name from the target's type. The relation builder
    ///     deduplicates within itself — <c>AddGeneratedProperty</c> says "avoid duplicates by name" —
    ///     so this is the half that was already covered, and it is here as the control.
    /// </remarks>
    [Fact]
    public void TwoRelationsToTheSameTarget_DoNotDeclareTheMemberTwice()
    {
        var result = Run($$"""
            {{Preamble}}

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Relation.ManyToOne<Person>]
            [Relation.ManyToOne<Person>]
            public partial class Ticket : IEntity
            {
                public string Subject { get; set; } = "";
            }
            """);

        Duplicates(result).Should().BeEmpty(
            "the relation builder already refuses to add a name it has added");
    }

    /// <summary>
    ///     ⚠️ Two <b>different</b> generators, one name.
    /// </summary>
    /// <remarks>
    ///     <c>[Auditable]</c> adds <c>CreatedBy</c> as a <c>string?</c>; a relation navigation called
    ///     <c>CreatedBy</c> adds it as a <c>Person</c>. Nothing compares across features, and the two
    ///     land in two partial parts of the same class — which is the case the dedup above cannot
    ///     see, because it only knows about the relations.
    /// </remarks>
    [Fact]
    public void ANavigationNamedLikeATraitMember_DoesNotDeclareTheMemberTwice()
    {
        var result = Run($$"""
            {{Preamble}}

            [Entity]
            [Auditable]
            [BelongsTo<SalesBoundary>]
            [Relation.ManyToOne<Person>.WithNavigation("CreatedBy")]
            public partial class Ticket : IEntity
            {
                public string Subject { get; set; } = "";
            }
            """);

        Duplicates(result).Should().BeEmpty(
            "a member declared twice across two generators' output is still a member declared twice");
    }

    /// <summary>The CS0102s, which is what "declared twice" looks like to a consumer.</summary>
    private static string[] Duplicates(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result)
            .Select(d => d.ToString())
            .Where(e => e.Contains("CS0102", StringComparison.Ordinal))
            .ToArray();

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<Persistence.Entity.IEntity>(),
                GeneratorTestHelper.FromType<Persistence.Entity.EntityAttribute>(),
                GeneratorTestHelper.FromType<Persistence.Entity.BelongsToAttribute<object>>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Result.Result<,>)),
                GeneratorTestHelper.FromType<Result.IError>(),
                GeneratorTestHelper.FromType<Specification.Specification<object>>(),
            ]);
}
