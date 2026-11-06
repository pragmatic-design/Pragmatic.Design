// Pragmatic.Composition.HostWiring.Tests - A host the engines alone persist registers its modules' generated values

using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host composed over a module the Sql engine persists, with no EF Core, calls the module's <c>[GeneratedValue]</c> registration (#571).
/// </summary>
/// <remarks>
///     The host called a module's persistence registrations from <c>RegisterAllRepositories</c>, which it wrote only when it found EF Core's
///     repositories. A host on the engines finds none (an engine registers its own in <c>Add{Boundary}Persistence</c>), so the method was not
///     written, no binding was registered, and every generated number stayed empty. The Showcase on the Sql engine is where it showed: the
///     second invoice violated its unique index.
/// </remarks>
public sealed class AnEngineOnlyHostRegistersItsModulesGeneratedValuesTests
{
    private const string Module = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Reviews
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class ReviewsBoundary;

            [Module(Name = "Shop.Reviews")]
            public sealed class ReviewsModule;
        }

        namespace Shop.Reviews.Entities
        {
            [Entity]
            public partial class Review : IEntity
            {
                [GeneratedValue("REV-{SEQ:4}")]
                public string Number { get; private set; } = "";
            }
        }
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using Shop.Reviews;

        namespace Shop.Host;

        [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Reviews")]
        public sealed class ReviewsDatabase : PragmaticDatabase;

        [Module]
        [Include<ReviewsModule, ReviewsDatabase>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    private static readonly MetadataReference[] Bridge = [HostWiringFixture.EngineBridge("Pragmatic.Persistence.Sql")];

    [Fact]
    public void TheHost_CallsTheModulesValueGeneratorRegistration()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Reviews", [Module], "Shop.Host", Host, Bridge, IsEfCore);

        errors.Should().BeEmpty();
        generated.Values.Should().Contain(v => v.Contains("GeneratedValueRegistrationExtensions.Add", StringComparison.Ordinal) && v.Contains("ValueGenerators(services);", StringComparison.Ordinal),
            "the module's [GeneratedValue] bindings are registered by the host, EF Core or not");
        generated.Values.Should().Contain(v => v.Contains("services.RegisterAllRepositories();", StringComparison.Ordinal),
            "and the method that registers them is called");
    }

    /// <summary>EF Core and everything of Pragmatic's built on it, by file name.</summary>
    private static bool IsEfCore(string fileName)
        => fileName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Npgsql.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
           || fileName.EndsWith(".EFCore.dll", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("Pragmatic.Identity.Persistence.dll", StringComparison.OrdinalIgnoreCase);
}
