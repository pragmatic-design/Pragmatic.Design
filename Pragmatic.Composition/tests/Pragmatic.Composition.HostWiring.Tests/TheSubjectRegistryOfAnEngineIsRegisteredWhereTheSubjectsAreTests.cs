// Pragmatic.Composition.HostWiring.Tests - The subject registry of an engine's boundary is registered where its subjects are

using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host whose <c>[DataSubject]</c> entities live in one boundary of the Sql or documents engine registers the subject registry and the
///     consent store on that boundary's session, as it registers EF Core's on the one database that holds them. Two places, and the host
///     registers none and says so. Registered before the <c>configure</c> callback: the registration replaces <c>AddPrivacy</c>'s fallback, and an
///     application's own registry, registered there, replaces it in turn.
/// </summary>
public sealed class TheSubjectRegistryOfAnEngineIsRegisteredWhereTheSubjectsAreTests
{
    private const string SqlMarker = """

        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    private static string Module(string engine, bool dataSubject) => $$"""
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Customers
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.{{engine}})]
            public partial class CustomersBoundary;

            [Module(Name = "Shop.Customers")]
            public sealed class CustomersModule;
        }

        namespace Shop.Customers.Entities
        {
            [Entity] {{(dataSubject ? "[Pragmatic.Privacy.DataSubject(nameof(Email))]" : "")}}
            public partial class Customer : IEntity
            {
                public string Email { get; private set; } = "";
            }
        }
        """ + (engine == "Sql" ? SqlMarker : "");

    private const string TwoSqlBoundaries = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Customers
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class CustomersBoundary;

            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class StaffBoundary;

            [Module(Name = "Shop.Customers")]
            public sealed class CustomersModule;
        }

        namespace Shop.Customers.Entities
        {
            [Entity] [Pragmatic.Privacy.DataSubject(nameof(Email))] [BelongsTo<Shop.Customers.CustomersBoundary>]
            public partial class Customer : IEntity
            {
                public string Email { get; private set; } = "";
            }

            [Entity] [Pragmatic.Privacy.DataSubject(nameof(Number))] [BelongsTo<Shop.Customers.StaffBoundary>]
            public partial class Employee : IEntity
            {
                public string Number { get; private set; } = "";
            }
        }
        """ + SqlMarker;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Shop.Customers;

        namespace Shop.Host;

        [Module]
        [Include<CustomersModule>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    private static readonly MetadataReference PrivacyEngines = HostWiringFixture.EngineBridge("Pragmatic.Privacy.Engines");

    [Fact]
    public void OneSqlBoundaryWithDataSubjects_GetsTheRegistry_BeforeTheApplicationsConfiguration()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Customers", [Module("Sql", true)], "Shop.Host", Host, [PrivacyEngines]);

        errors.Should().BeEmpty("the host has to compile, or the registry is registered by text nobody runs");
        RegisterSubjectRegistry(generated).Should().Contain(
            "global::Pragmatic.Privacy.Engines.Sql.SqlPrivacyExtensions.AddSqlSubjectRegistry<global::Shop.Customers.CustomersBoundary>(services);",
            "the boundary's database holds the registry's tables");

        var entry = generated.Values.Single(v => v.Contains("configure?.Invoke(pragmaticBuilder);", StringComparison.Ordinal)
                                                 && v.Contains("PragmaticHost.RegisterSubjectRegistry(builder.Services);", StringComparison.Ordinal));
        var registry = entry.IndexOf("PragmaticHost.RegisterSubjectRegistry(builder.Services);", StringComparison.Ordinal);
        registry.Should().BeGreaterThan(entry.IndexOf("PragmaticHost.RegisterAllPragmaticServices(", StringComparison.Ordinal),
            "after AddPrivacy's fallback, which it replaces");
        registry.Should().BeLessThan(entry.IndexOf("configure?.Invoke(pragmaticBuilder);", StringComparison.Ordinal),
            "before the application's configuration, whose own registry replaces it");
    }

    [Fact]
    public void OneDocumentsBoundaryWithDataSubjects_GetsTheRegistryOnItsSession()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Customers", [Module("Documents", true)], "Shop.Host", Host,
            [PrivacyEngines, HostWiringFixture.EngineBridge("Pragmatic.Persistence.Documents")]);

        errors.Should().BeEmpty();
        RegisterSubjectRegistry(generated).Should().Contain(
            "global::Pragmatic.Privacy.Engines.Documents.DocPrivacyExtensions.AddDocSubjectRegistry<global::Shop.Customers.CustomersBoundary>(services);");
    }

    [Fact]
    public void TwoBoundariesWithDataSubjects_RegisterNoRegistry_AndSaySo()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Customers", [TwoSqlBoundaries], "Shop.Host", Host, [PrivacyEngines]);

        errors.Should().BeEmpty();
        var method = RegisterSubjectRegistry(generated);
        method.Should().NotContain(">(services);", "the registry is one, and which of the two places is the application's to name");
        method.Should().Contain("[DataSubject] entities live in more than one place");
    }

    [Fact]
    public void ABoundaryWithoutDataSubjects_HasNoRegistryToRegister()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Customers", [Module("Sql", false)], "Shop.Host", Host, [PrivacyEngines]);

        errors.Should().BeEmpty();
        generated.Values.Should().NotContain(v => v.Contains("RegisterSubjectRegistry", StringComparison.Ordinal),
            "no entity is a data subject, so there is no registry to hold");
    }

    private static string RegisterSubjectRegistry(IReadOnlyDictionary<string, string> generated)
    {
        var host = generated.Values.SingleOrDefault(v => v.Contains("RegisterSubjectRegistry(this IServiceCollection services)", StringComparison.Ordinal));
        host.Should().NotBeNull("an engine's boundary holds [DataSubject] entities, and the host decides where the registry is");
        var start = host!.IndexOf("RegisterSubjectRegistry(this IServiceCollection services)", StringComparison.Ordinal);
        return host.Substring(start, host.IndexOf("return services;", start, StringComparison.Ordinal) - start);
    }
}
