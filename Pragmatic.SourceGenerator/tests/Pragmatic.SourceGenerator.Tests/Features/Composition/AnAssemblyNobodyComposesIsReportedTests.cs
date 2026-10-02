using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A referenced assembly that declares a message-handler registration and is not a module is told
///     about at build time, instead of being absent at run time.
/// </summary>
/// <remarks>
///     <para>
///         The host composes the assemblies it <b>discovered</b>, and discovery is by module: an
///         assembly with no <c>[Module]</c> is filtered out with everything it declares. A contracts
///         project is deliberately not a module — it is what a consumer references, and it must not drag
///         the module in — so its generated <c>AddPragmaticMessageHandlers</c> exists, compiles, is
///         correct, and is called by nobody.
///     </para>
///     <para>
///         ⚠️ Measured on Casework: the registration carries the message type registry the
///         outbox pump resolves types through, and with it uncalled <b>six of the suite's tests
///         failed</b> — every cross-service delivery — with nothing at build time pointing anywhere. The
///         first sign was a message that never arrived.
///     </para>
///     <para>
///         Whether discovery should follow the metadata rather than the module is a decision about
///         every host — measured across this repository as <b>12</b> non-module assemblies that emit
///         metadata, nine of them framework packages — and is not taken here. What this closes is the silence.
///     </para>
/// </remarks>
public class AnAssemblyNobodyComposesIsReportedTests
{
    private const string Diagnostic = "PRAG1699";

    /// <summary>What both compilations need: the attributes, by full name, as the generator matches them.</summary>
    private const string Stubs = """
        namespace Pragmatic.Composition.Hosting
        {
            // The marker CompositionDetector requires before it will generate a host at all: an Exe that
            // does not reference Composition.Host cannot host modules, so without this the run is
            // library mode and reports nothing — which is how the first version of this test came out
            // green-adjacent, with no diagnostics of any kind.
            public class PragmaticBuilder { }
        }
        namespace Pragmatic.Composition.Metadata
        {
            public enum MetadataCategory { Unknown = 0, MessageHandlers = 15 }
        }
        namespace Pragmatic.Composition.Database
        {
            public abstract class PragmaticDatabase { }
        }
        namespace Pragmatic.Composition.Enums
        {
            public enum DatabaseProvider { SqlServer, PostgreSql, SQLite, MySql, InMemory }
        }
        namespace Pragmatic.Composition.Attributes
        {
            using Pragmatic.Composition.Database;
            using Pragmatic.Composition.Enums;
            using Pragmatic.Composition.Metadata;

            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class PragmaticMetadataAttribute : System.Attribute
            {
                public PragmaticMetadataAttribute(MetadataCategory category, string schemaVersion, string jsonData) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class PragmaticDatabaseAttribute : System.Attribute
            {
                public DatabaseProvider Provider { get; set; }
                public string? ConfigKey { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class ModuleAttribute : System.Attribute
            {
                public string? Name { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class IncludeAttribute<TModule, TDatabase> : System.Attribute
                where TModule : class where TDatabase : PragmaticDatabase { }
        }
        """;

    /// <summary>
    ///     The contracts assembly: it declares a messaging registration and is <b>not</b> a module.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The assembly attribute is first and fully qualified. It has to precede everything but
    ///     usings, and the stubs arrive through a reference rather than through this file — two
    ///     assemblies each declaring <c>PragmaticMetadataAttribute</c> are two different types, and the
    ///     reader compares the attribute's class with the one it resolved by name: they would not match
    ///     and the test would measure nothing while looking as if it did.
    /// </remarks>
    private const string Contracts = """
        [assembly: Pragmatic.Composition.Attributes.PragmaticMetadata(
            Pragmatic.Composition.Metadata.MetadataCategory.MessageHandlers, "1.0",
            "{\"generator\":\"test\",\"registrationMethod\":\"Contracts.Generated.PragmaticMessageHandlerRegistration.AddPragmaticMessageHandlers\"}")]

        namespace Contracts.Generated
        {
            public static class PragmaticMessageHandlerRegistration { }
        }
        """;

    /// <summary>The host: one module of its own, included — so the include filter is active.</summary>
    private const string Host = """

        namespace App
        {
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Database;
            using Pragmatic.Composition.Enums;

            public sealed class SalesBoundary { }

            [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
            public sealed class AppDatabase : PragmaticDatabase { }

            [Module(Name = "AppHost")]
            [Include<SalesBoundary, AppDatabase>]
            public sealed class HostModule { }
        }

        internal static class Program { private static void Main() { } }
        """;

    [Fact]
    public void AContractsAssemblyTheHostDoesNotCompose_IsReported()
        => IdsOf(RunAsHost(Host, Attributes, WithContracts()))
            .Should().Contain(Diagnostic,
                "the registration is generated, correct and called by nobody, and nothing else says so");

    /// <summary>And the message names the assembly and the method, because a warning without them is a search.</summary>
    [Fact]
    public void TheReport_NamesTheAssemblyAndTheMethodToCall()
    {
        var message = RunAsHost(Host, Attributes, WithContracts()).Diagnostics
            .Where(d => d.Id == Diagnostic)
            .Select(d => d.GetMessage())
            .Should().ContainSingle().Subject;

        message.Should().Contain("Contracts", "which assembly is not composed");
        message.Should().Contain("AddPragmaticMessageHandlers", "and the one line that fixes it");
    }

    /// <summary>
    ///     The control that keeps the warning satisfiable: a host that already calls the registration is
    ///     not told to call it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the shape every application in this repository has today — the line in
    ///     <c>Program.cs</c> is exactly the fix the message recommends. A diagnostic that fires on the
    ///     application that has done what it asks is one people suppress, and a suppressed diagnostic is
    ///     the silence it replaced with an extra step.
    /// </remarks>
    [Fact]
    public void AHostThatAlreadyCallsIt_IsNotReported()
        => IdsOf(RunAsHost(Host + CallsTheRegistration, Attributes, WithContracts()))
            .Should().NotContain(Diagnostic);

    private const string CallsTheRegistration = """

        namespace App
        {
            internal static class Wiring
            {
                public static void Wire()
                    => global::Contracts.Generated.PragmaticMessageHandlerRegistration
                        .AddPragmaticMessageHandlers();
            }
        }
        """;

    /// <summary>
    ///     The control: an assembly with no messaging registration is not reported, however little the
    ///     host composes of it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without this, "a referenced assembly is reported" would be satisfied by a diagnostic that
    ///     fires on every assembly outside the include set — which is most of them, framework packages
    ///     included, and a warning on all of them is a warning on none.
    /// </remarks>
    [Fact]
    public void AnAssemblyWithNoMessagingRegistration_IsNotReported()
    {
        var quiet = GeneratorTestHelper.CompileReference("Quiet", """
            namespace Quiet.Things
            {
                public static class Nothing { }
            }
            """, Attributes);

        IdsOf(RunAsHost(Host, Attributes, quiet)).Should().NotContain(Diagnostic);
    }

    /// <summary>
    ///     The control that keeps it about assemblies nobody <b>could</b> include: a module the host
    ///     left out of its <c>[Include]</c> list was left out on purpose.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Measured on the first clean build after this diagnostic existed: it reported
    ///     <c>Showcase.Booking</c> to <c>Showcase.Billing.Host</c>, which is the distributed Showcase
    ///     hosting Billing and not Booking — a decision, not a defect, and the attribute exists to make
    ///     it. What has no such decision behind it is an assembly that could not have been included
    ///     whatever the host wrote, because it is not a module.
    /// </remarks>
    [Fact]
    public void AModuleTheHostChoseNotToInclude_IsNotReported()
    {
        var otherModule = GeneratorTestHelper.CompileReference("Other", """
            [assembly: Pragmatic.Composition.Attributes.PragmaticMetadata(
                (Pragmatic.Composition.Metadata.MetadataCategory)8, "1.0", "{\"name\":\"Other\"}")]
            [assembly: Pragmatic.Composition.Attributes.PragmaticMetadata(
                Pragmatic.Composition.Metadata.MetadataCategory.MessageHandlers, "1.0",
                "{\"registrationMethod\":\"Other.Generated.PragmaticMessageHandlerRegistration.AddPragmaticMessageHandlers\"}")]

            namespace Other.Generated
            {
                public static class PragmaticMessageHandlerRegistration { }
            }
            """, Attributes);

        IdsOf(RunAsHost(Host, Attributes, otherModule)).Should().NotContain(Diagnostic,
            "leaving a module out of [Include<T>] is what the attribute is for");
    }

    /// <summary>
    ///     The second control: a host with no <c>[Include]</c> filters nothing, so it composes that
    ///     assembly and there is nothing to report.
    /// </summary>
    /// <remarks>
    ///     The filter is what drops the assembly, and it only runs when the host declares a topology.
    ///     A report that fired without it would be about an assembly the host does call.
    /// </remarks>
    [Fact]
    public void AHostThatFiltersNothing_IsNotReported()
        => IdsOf(RunAsHost(
                "internal static class Program { private static void Main() { } }",
                Attributes, WithContracts()))
            .Should().NotContain(Diagnostic);

    /// <summary>
    ///     The attributes, compiled once into an assembly of their own.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Once, and shared. Declaring them in both compilations gives two distinct
    ///     <c>PragmaticMetadataAttribute</c> types: the attribute on the referenced assembly binds to
    ///     one and <c>GetTypeByMetadataName</c> resolves the other, so the reader finds no entries and
    ///     every test here passes or fails for a reason that has nothing to do with the subject.
    /// </remarks>
    private static readonly MetadataReference Attributes =
        GeneratorTestHelper.CompileReference("Pragmatic.Composition.Attributes.Stubs", Stubs);

    private static MetadataReference WithContracts()
        => GeneratorTestHelper.CompileReference("Contracts", Contracts, Attributes);

    private static SourceGenRunResult RunAsHost(string source, params MetadataReference[] references)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(source, references);

    private static string[] IdsOf(SourceGenRunResult result)
        => [.. result.Diagnostics.Select(d => d.Id)];
}
