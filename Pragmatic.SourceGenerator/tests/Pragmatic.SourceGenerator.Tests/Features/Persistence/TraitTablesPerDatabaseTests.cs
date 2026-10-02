using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A trait table belongs to the database of the boundary that owns its parent, and to that one only.
/// </summary>
/// <remarks>
///     A migration context receives only the trait entities of its own boundaries, selected by the
///     model's <c>BoundaryName</c>. Handing every context the whole set would make both migrations of a
///     two-database topology create <c>GuestComments</c> and the rest, with only one of them ever
///     written to. The single-database case is the control — there the same trait must still appear.
/// </remarks>
public class TraitTablesPerDatabaseTests
{
    private const string AbstractionsSource = """
        namespace Pragmatic.Actions.Metadata
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticModuleMetadataAttribute : System.Attribute
            {
                public System.Type? BoundaryType { get; set; }
            }
        }
        namespace Pragmatic.Composition.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ModuleAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class IncludeAttribute<TModule, TDatabase> : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDatabaseAttribute : System.Attribute
            {
                public string? ConfigKey { get; set; }
            }
        }
        namespace Pragmatic.Persistence.EFCore
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : System.Attribute { }
        }
        namespace Pragmatic.Persistence.Entity
        {
            public sealed class EntityAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class BelongsToAttribute<TBoundary> : System.Attribute { }
        }
        namespace Contoso.Meta
        {
            // Deliberately NOT in Pragmatic.Composition.Attributes: the trait reader matches on the
            // simple name only, and keeping it out of that namespace avoids flipping HasComposition.
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticMetadataAttribute : System.Attribute
            {
                public PragmaticMetadataAttribute(int category, string name, string payload) { }
            }
        }
        """;

    /// <summary>Booking owns Guest, and Guest carries comments — so the trait table is Booking's.</summary>
    private const string BookingAssemblySource = """
        [assembly: Pragmatic.Actions.Metadata.PragmaticModuleMetadata(
            BoundaryType = typeof(Contoso.Booking.BookingBoundary))]
        [assembly: Contoso.Meta.PragmaticMetadata(19, "TraitEntities",
            "[{\"TypeName\":\"GuestComment\",\"Namespace\":\"Contoso.Booking\",\"BoundaryName\":\"Booking\",\"TraitKind\":\"Comment\"}]")]

        namespace Contoso.Booking
        {
            public sealed class BookingBoundary { }
            public sealed class BookingModule { }

            [Pragmatic.Persistence.Entity.Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<BookingBoundary>]
            public class Guest { public string Name { get; set; } = ""; }
        }
        """;

    private const string BillingAssemblySource = """
        [assembly: Pragmatic.Actions.Metadata.PragmaticModuleMetadata(
            BoundaryType = typeof(Contoso.Billing.BillingBoundary))]

        namespace Contoso.Billing
        {
            public sealed class BillingBoundary { }
            public sealed class BillingModule { }

            [Pragmatic.Persistence.Entity.Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<BillingBoundary>]
            public class Invoice { public string Number { get; set; } = ""; }
        }
        """;

    private static string HostSource(string billingDatabase) => $$"""
        namespace Contoso.Host
        {
            [Pragmatic.Composition.Attributes.PragmaticDatabase(ConfigKey = "ConnectionStrings:App")]
            public sealed class AppDatabase { }

            [Pragmatic.Composition.Attributes.PragmaticDatabase(ConfigKey = "ConnectionStrings:Financial")]
            public sealed class FinancialDatabase { }

            [Pragmatic.Composition.Attributes.Module]
            [Pragmatic.Composition.Attributes.Include<Contoso.Booking.BookingModule, AppDatabase>]
            [Pragmatic.Composition.Attributes.Include<Contoso.Billing.BillingModule, {{billingDatabase}}>]
            public sealed class HostModule { }

            public static class Program { public static void Main() { } }
        }
        """;

    [Fact]
    public void TwoDatabases_TheTraitTableIsOnlyInItsOwnMigrationContext()
    {
        var sources = MigrationContextsFor("FinancialDatabase");

        var withTheTrait = sources.Where(kv => kv.Value.Contains("GuestComment")).Select(kv => kv.Key).ToList();

        withTheTrait.Should().ContainSingle(
            "the trait belongs to Booking's database and to no other; it was in every context")
            .Which.Should().Contain("AppDatabase");
    }

    [Fact]
    public void OneDatabase_TheTraitTableIsStillThere()
    {
        var sources = MigrationContextsFor("AppDatabase");

        sources.Values.Count(text => text.Contains("GuestComment")).Should().Be(1,
            "with both modules on one database the single migration context still creates the table");
    }

    /// <summary>Every generated migration DbContext, by hint name.</summary>
    private static Dictionary<string, string> MigrationContextsFor(string billingDatabase)
    {
        var abstractions = CompileToReference("Contoso.Abstractions", AbstractionsSource);
        var booking = CompileToReference("Contoso.Booking", BookingAssemblySource, abstractions);
        var billing = CompileToReference("Contoso.Billing", BillingAssemblySource, abstractions);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            HostSource(billingDatabase), abstractions, booking, billing);

        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("MigrationDbContext") || kv.Key.Contains("DbContext."))
            .Where(kv => kv.Value.Contains("MigrationDbContext"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static MetadataReference CompileToReference(
        string assemblyName, string source, params MetadataReference[] dependencies)
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "netstandard.dll")),
            .. dependencies
        ];

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, path: $"{assemblyName}.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        emitResult.Success.Should().BeTrue(
            "the reference assembly {1} must compile: {0}",
            string.Join(Environment.NewLine, emitResult.Diagnostics), assemblyName);

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
