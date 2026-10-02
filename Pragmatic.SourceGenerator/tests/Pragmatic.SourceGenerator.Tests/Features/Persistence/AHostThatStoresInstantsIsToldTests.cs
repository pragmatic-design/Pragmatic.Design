using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A host that persists instants without the convention that normalises them is told so.
/// </summary>
/// <remarks>
///     <para>
///         Everything that converts an instant on the way out reads the stored value as UTC. The
///         convention that makes it true ships in <c>Pragmatic.Temporal.EFCore</c>, which is optional —
///         so without <c>PRAG0690</c> an application can persist for years with nothing normalising the
///         column, and no exception anywhere: a payload wrong by an offset, and by an hour more twice a
///         year.
///     </para>
///     <para>
///         ⚠️ Host mode, and that is the whole design. Whether the normalisation is wired is a property
///         of the host; the module that declares the entity cannot see it, so the same check there
///         would be false for every solution whose host references the package.
///     </para>
/// </remarks>
public class AHostThatStoresInstantsIsToldTests
{
    private const string Stubs = """
        namespace Pragmatic.Composition.Hosting { public class PragmaticBuilder { } }

        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }
        }

        namespace Pragmatic.Persistence.EFCore
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : System.Attribute { }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
        }

        public static class Program { public static void Main() { } }
        """;

    private const string TemporalEfCoreStub = """

        namespace Pragmatic.Temporal.EntityFrameworkCore.Conventions
        {
            public static class TemporalPropertyRegistry { }
        }
        """;

    private const string AnEntityWithAnInstant = """

        namespace App.Sales
        {
            [Pragmatic.Persistence.Entity.Entity]
            public partial class Invoice
            {
                public System.Guid Id { get; set; }
                public System.DateTimeOffset IssuedAt { get; set; }
            }
        }
        """;

    /// <summary>A host storing an instant, with nothing normalising it, is warned once.</summary>
    [Fact]
    public void AHostStoringAnInstant_WithoutTheConvention_IsWarned()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Stubs + AnEntityWithAnInstant, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0690").Should().BeTrue();
    }

    /// <summary>
    ///     The control: with the package referenced it says nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "the host is warned" is satisfied by a diagnostic that fires on every host, which
    ///     is the shape a developer switches off within a day.
    /// </remarks>
    [Fact]
    public void WithTheConventionReferenced_NothingIsSaid()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Stubs + TemporalEfCoreStub + AnEntityWithAnInstant, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0690").Should().BeFalse();
    }

    /// <summary>
    ///     The second control: a host whose entities store no instant hears nothing either.
    /// </summary>
    [Fact]
    public void AHostWithNoInstantToStore_IsNotWarned()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Stubs + """

            namespace App.Sales
            {
                [Pragmatic.Persistence.Entity.Entity]
                public partial class Tag
                {
                    public System.Guid Id { get; set; }
                    public string Name { get; set; } = "";
                }
            }
            """, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0690").Should().BeFalse();
    }
}
