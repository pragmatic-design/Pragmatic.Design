using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>[TenantSource]</c> entity gets its <c>ITenantStore</c> generated, declared to the host, and registered there in place of the
///     empty in-memory store (#645).
/// </summary>
/// <remarks>
///     Without it the guards on the tenant mean nothing: the generated host registered an empty <c>InMemoryTenantStore</c>, so every tenant
///     was unknown until the application wrote the same read view over its own table by hand.
/// </remarks>
public class ATenantSourceGetsItsStoreTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<TenantSourceAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
        GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>()
    ];

    /// <summary>Marks the compilation as referencing Composition, and the host as resolving tenants.</summary>
    private const string Stubs = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        namespace Pragmatic.MultiTenancy
        {
            public sealed class InMemoryTenantStore { }
        }
        """;

    private const string Company = """
        [Entity]
        [Auditable]
        [TenantSource(Id = nameof(Slug), Name = nameof(LegalName), State = nameof(State))]
        public partial class Company : IEntity
        {
            public string Slug { get; set; } = "";
            public string LegalName { get; set; } = "";
            public TenantState State { get; set; } = TenantState.Active;
        }
        """;

    private const string Workspace = """
        [Entity]
        [TenantSource(Id = nameof(Code), Name = nameof(Title), State = nameof(State))]
        public partial class Workspace : IEntity
        {
            public string Code { get; set; } = "";
            public string Title { get; set; } = "";
            public TenantState State { get; set; } = TenantState.Active;
        }
        """;

    private static string Source(string entities) => Stubs + $$"""

        namespace Contoso.Registry
        {
            using Pragmatic.Actions.Attributes;
            using Pragmatic.MultiTenancy;
            using Pragmatic.Persistence.Entity;

            [Boundary]
            public partial class RegistryBoundary;

            {{entities}}
        }
        """;

    private static SourceGenRunResult Run(string entities)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(entities), References);

    private static SourceGenRunResult RunAsHost(string entities)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Source(entities) + """

            public static class Program
            {
                public static void Main() { }
            }
            """, References);

    private static string? Generated(SourceGenRunResult result, string hint)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains(hint))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    private static IEnumerable<string> ErrorsIn(SourceGenRunResult result, string hint)
        => GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains(hint) == true)
            .Select(d => $"{d.Id}: {d.GetMessage()}");

    [Fact]
    public void ADeclaredTenantSource_GetsAReadOnlyStore_ThatCompiles()
    {
        var result = Run(Company);

        var store = Generated(result, "Company.TenantStore");
        store.Should().NotBeNull();
        store!.Should().Contain(": global::Pragmatic.MultiTenancy.IEntityTenantStore");
        store.Should().Contain(".FirstOrDefaultAsync(global::Contoso.Registry.CompanySpecifications.ByTenantSourceId(tenantId), ct)",
            "a tenant is found through a specification, which every engine serves");
        store.Should().Contain("CreateAsyncScope(scopes)",
            "the store is a singleton and the repository is scoped: each read opens its own scope rather than capturing one");
        store.Should().Contain("TenantId = row.Slug").And.Contain("TenantName = row.LegalName").And.Contain("State = row.State");
        store.Should().Contain("CreatedAt = row.CreatedAt", "an [Auditable] entity's CreatedAt is when the tenant was registered");
        store.Should().Contain("throw new global::System.NotSupportedException(",
            "the writes refuse: a tenant's row is written by the application's own operations");
        ErrorsIn(result, "Company.TenantStore").Should().BeEmpty();

        Generated(result, "Company.Specs").Should().Contain("e => e.Slug == tenantId");
    }

    [Fact]
    public void AModuleWithATenantSource_NamesItsStoreInItsMetadata()
        => Generated(Run(Company), "_Metadata.TenantSource").Should()
            .Contain("\"registrationMethod\": \"global::Contoso.Registry.Company.TenantStore.Create\"");

    /// <summary>The control: an entity that declares nothing gets no store and no metadata.</summary>
    [Fact]
    public void AnEntityWithoutTheAttribute_GetsNoStore()
    {
        var plain = Company.Replace("[TenantSource(Id = nameof(Slug), Name = nameof(LegalName), State = nameof(State))]", "");
        var result = Run(plain);

        Generated(result, "Company.TenantStore").Should().BeNull();
        Generated(result, "_Metadata.TenantSource").Should().BeNull();
    }

    [Fact]
    public void TheHost_RegistersTheStore_InPlaceOfTheInMemoryOne_StillObserved()
    {
        var host = Generated(RunAsHost(Company), "Host.Services");

        host.Should().NotBeNull();
        host!.Should().Contain("new global::Pragmatic.MultiTenancy.ObservedTenantStore(");
        host.Should().Contain("global::Contoso.Registry.Company.TenantStore.Create(sp),");
        host.Should().NotContain("new global::Pragmatic.MultiTenancy.InMemoryTenantStore()");
    }

    [Fact]
    public void AMemberOfTheWrongType_IsPRAG0642_AndNoStoreIsWritten()
    {
        var wrong = Company.Replace("public TenantState State { get; set; } = TenantState.Active;", "public string State { get; set; } = \"\";");
        var result = Run(wrong);

        result.Diagnostics.Select(d => d.Id).Should().Contain("PRAG0642");
        Generated(result, "Company.TenantStore").Should().BeNull();
    }

    [Fact]
    public void AMemberThatDoesNotExist_IsPRAG0642()
        => Run(Company.Replace("Name = nameof(LegalName)", "Name = \"DisplayName\"")).Diagnostics.Select(d => d.Id).Should().Contain("PRAG0642");

    [Fact]
    public void TwoSourcesInOneHost_ArePRAG1617_AndTheHostKeepsTheInMemoryStore()
    {
        var result = RunAsHost(Company + Workspace);

        result.Diagnostics.Select(d => d.Id).Should().Contain("PRAG1617");
        Generated(result, "Host.Services").Should().Contain("new global::Pragmatic.MultiTenancy.InMemoryTenantStore()");
    }
}
