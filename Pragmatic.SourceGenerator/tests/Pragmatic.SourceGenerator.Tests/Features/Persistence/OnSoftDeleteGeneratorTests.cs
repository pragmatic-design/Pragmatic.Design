using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Composition.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A relation's <c>OnSoftDelete</c> reaches the EF Core model as the rule <c>SoftDeleteInterceptor</c> applies (#617).
/// </summary>
/// <remarks>
///     <para>
///         The rule is emitted on the dependent's configuration, typed, and compiled here with the rest of the host's
///         output: a query naming a key that does not exist, or an assignment of null to a required key, is a compile
///         error in code the application did not write. What the interceptor does with the rule is measured in
///         <c>ASoftDeleteHonoursOnSoftDeleteTests</c>.
///     </para>
///     <para>
///         The declaration travels to the host through the module's metadata when the entities live in another
///         assembly, which is the shape of every application: the second half of these tests puts them there.
///     </para>
/// </remarks>
public class OnSoftDeleteGeneratorTests
{
    private const string Rule = "global::Pragmatic.Persistence.EFCore.SoftDelete.SoftDeleteReferrerRule";

    /// <remarks>
    ///     A dropped reference does not fail loudly: the generated rule stops compiling, and the assertion on the
    ///     errors below is what reads it. <c>Pragmatic.Persistence.EFCore</c> carries the rule type itself.
    /// </remarks>
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        .. ByName(
            "System.Text.Json",
            "System.ComponentModel.TypeConverter",
            "System.Linq",
            "System.Linq.Queryable",
            "System.Linq.Expressions",
            "System.ComponentModel.Annotations",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Actions"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));

    private const string Entities = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Contoso.Library;

        [Boundary]
        public partial class LibraryBoundary;

        [Entity]
        [BelongsTo<LibraryBoundary>]
        [SoftDelete]
        [Relation.OneToMany<Book>.WithNavigation("Books", OnSoftDelete = DeleteBehavior.Restrict)]
        [Relation.OneToMany<Label>.WithNavigation("Labels", Inverse = "Shelf", OnSoftDelete = DeleteBehavior.SetNull)]
        [Relation.OneToMany<Poster>.WithNavigation("Posters")]
        public partial class Shelf : IEntity { }

        [Entity]
        [BelongsTo<LibraryBoundary>]
        [SoftDelete]
        public partial class Book : IEntity { }

        [Entity]
        [BelongsTo<LibraryBoundary>]
        [Relation.ManyToOne<Shelf>.WithNavigation("Shelf", Required = false)]
        public partial class Label : IEntity { }

        [Entity]
        [BelongsTo<LibraryBoundary>]
        public partial class Poster : IEntity { }
        """;

    /// <summary>The entry point that makes a compilation a host; appended under the entities' namespace, or alone.</summary>
    private const string Program = """

        public static class Program { public static void Main() { } }
        """;

    [Fact]
    public void ARestrictIsPutOnTheDependentsForeignKey_AndCompiles()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Entities + Program, References);

        ConfigurationOf(result, "Book").Should().Contain(
            $"new {Rule}<global::Contoso.Library.Shelf, global::Contoso.Library.Book>(")
            .And.Contain("global::Pragmatic.Persistence.Entity.DeleteBehavior.Restrict,");

        ConfigurationErrors(result).Should().BeEmpty();
    }

    /// <summary>
    ///     A <c>SetNull</c> declared on the parent's collection lands on the child's key, which the child declares
    ///     optional. The key is emptied through the tracker, so no assignment is emitted: the generated key's setter
    ///     is not public, and an assignment here was CS0272 the first time this compiled.
    /// </summary>
    [Fact]
    public void ASetNullIsPutOnTheChildsOptionalKey()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Entities + Program, References);

        ConfigurationOf(result, "Label").Should().Contain("DeleteBehavior.SetNull,")
            .And.Contain("d => d.ShelfId == principal.PersistenceId");

        ConfigurationErrors(result).Should().BeEmpty();
    }

    /// <summary>The control: a relation that declares no <c>OnSoftDelete</c> carries no rule.</summary>
    [Fact]
    public void WithoutOnSoftDelete_NoRuleIsEmitted()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Entities + Program, References);

        var poster = ConfigurationOf(result, "Poster");
        poster.Should().Contain(".OnDelete(", "the relationship is configured, so the absence below is about the rule");
        poster.Should().NotContain(Rule);
    }

    /// <summary>
    ///     The entities in a module of their own, as in an application: the declaration reaches the host's
    ///     configuration through the module's metadata.
    /// </summary>
    [Fact]
    public void FromAReferencedModule_TheRuleReachesTheHostsConfiguration()
    {
        // The implicit usings of an SDK project: the module's generated filter map calls Where without naming
        // System.Linq, which a project created by `dotnet new` imports globally.
        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Contoso.Library", "global using System.Linq;\n" + Entities, References);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Program, [.. References, module]);

        ConfigurationOf(result, "Book").Should().Contain(
            $"new {Rule}<global::Contoso.Library.Shelf, global::Contoso.Library.Book>(");
        ConfigurationOf(result, "Label").Should().Contain("DeleteBehavior.SetNull,");
        ConfigurationErrors(result).Should().BeEmpty();
    }

    /// <summary>The well-formed declarations above report neither diagnostic: the control for the two below.</summary>
    [Fact]
    public void WellFormedDeclarations_ReportNothing()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Entities + Program, References);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0640").Should().BeFalse();
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0641").Should().BeFalse();
    }

    /// <summary>PRAG0640: the label's key is required once <c>Required = false</c> is gone.</summary>
    [Fact]
    public void SetNullOnARequiredKey_IsReported()
    {
        var required = Entities.Replace("""WithNavigation("Shelf", Required = false)""", """WithNavigation("Shelf")""");
        required.Should().NotBe(Entities, "the fixture must have been rewritten for the case to measure anything");

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(required + Program, References);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0640").Should().BeTrue(
            "the soft delete would write null into a column that cannot hold it");
    }

    /// <summary>PRAG0641: a cascade towards the poster, which has no flag to raise.</summary>
    [Fact]
    public void CascadeTowardsAnEntityThatIsNotSoftDeletable_IsReported()
    {
        var cascading = Entities.Replace(
            """WithNavigation("Posters")""", """WithNavigation("Posters", OnSoftDelete = DeleteBehavior.Cascade)""");
        cascading.Should().NotBe(Entities, "the fixture must have been rewritten for the case to measure anything");

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(cascading + Program, References);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0641").Should().BeTrue(
            "a poster is not [SoftDelete], so it cannot be soft-deleted with its shelf");
    }

    private static string ConfigurationOf(SourceGenRunResult result, string entity)
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var match = sources.Where(kv => kv.Key.Contains("EntityConfig.") && kv.Key.Contains($".{entity}.")).ToList();
        match.Should().HaveCount(1, $"one configuration for {entity}; generated: {string.Join(", ", sources.Keys)}");
        return match[0].Value;
    }

    private static IEnumerable<string> ConfigurationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("EntityConfig.") == true)
            .Select(d => $"{d.Id}: {d.GetMessage()}");
}
