// Pragmatic.Composition.HostWiring.Tests - A host whose databases the engines alone persist compiles without EF Core

using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host composed over a module the Sql engine persists, with no EF Core among its references, compiles: the generated host writes
///     EF Core's query executor, its hints and its <c>using</c> only for a host that references Pragmatic.Persistence.EFCore (#552).
/// </summary>
/// <remarks>
///     Every other engine test of this suite compiles with EF Core in the reference set, which is why none of them saw the host write it
///     unconditionally: the Showcase host on the Sql engine (#534) was the first compilation without it.
/// </remarks>
public sealed class AHostOnTheEnginesAloneReferencesNoEfCoreTests
{
    private const string Module = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Reviews
        {
            // Named: in the control the module references EF Core too, and a boundary of a module with two engines names its own (PRAG0743).
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
                public string Text { get; private set; } = "";
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

    /// <summary>The Sql engine's bridge, for these compilations only.</summary>
    private static readonly MetadataReference[] Bridge = [HostWiringFixture.EngineBridge("Pragmatic.Persistence.Sql")];

    [Fact]
    public void AHostWithoutEfCore_Compiles_AndItsGeneratedCodeNamesNoEfCore()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Reviews", [Module], "Shop.Host", Host, Bridge, IsEfCore);

        errors.Should().BeEmpty("a host the engines alone persist references no EF Core, and its generated code may not name it");
        generated.Where(g => g.Value.Contains("EntityFrameworkCore", StringComparison.Ordinal) || g.Value.Contains("Pragmatic.Persistence.EFCore", StringComparison.Ordinal))
            .Select(g => g.Key).Should().BeEmpty();
    }

    /// <summary>The control: with Pragmatic.Persistence.EFCore referenced, the host still registers EF Core's query executor.</summary>
    [Fact]
    public void AHostWithEfCore_StillRegistersEfCoresQueryExecutor()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Reviews", [Module], "Shop.Host", Host, Bridge);

        errors.Should().BeEmpty();
        generated.Values.Should().Contain(v => v.Contains("new global::Pragmatic.Persistence.EFCore.Query.EfCoreQueryExecutor(", StringComparison.Ordinal),
            "a host that references EF Core keeps its executor");
    }

    /// <summary>EF Core and everything of Pragmatic's built on it, by file name.</summary>
    private static bool IsEfCore(string fileName)
        => fileName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("Npgsql.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
           || fileName.EndsWith(".EFCore.dll", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("Pragmatic.Identity.Persistence.dll", StringComparison.OrdinalIgnoreCase);
}
