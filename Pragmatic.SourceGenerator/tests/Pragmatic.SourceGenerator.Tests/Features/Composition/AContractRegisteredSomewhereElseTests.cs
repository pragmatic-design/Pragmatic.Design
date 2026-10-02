using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Composition.Attributes;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A module's <c>[Service]</c> may depend on something registered outside its own
///     compilation, and PRAG1641 has to answer "who registers this?" from a declaration rather than from
///     a list of names carried inside the generator.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the Invoicing example: <c>OverdueReminderSweep</c> was refused three times over —
///         <c>IRegistryReads</c> (generated and registered by the sibling module), <c>IFileStorage</c> and
///         <c>IEmailSender</c> (registered by the host's <c>UseStorage</c>/<c>UseEmail</c>) — although the
///         running application resolves all three. The example had to register the class by hand, which
///         is the opposite of what <c>[Service]</c> exists for.
///     </para>
///     <para>
///         ⚠️ The two halves fail for different reasons and are fixed differently. A contract in a
///         package says it itself, with <c>[ProvidedByHost]</c>; what another <b>module</b> registers is
///         read from that module's own DI metadata, which it already emits — the generator does not have
///         to be told a name it can look up.
///     </para>
/// </remarks>
public class AContractRegisteredSomewhereElseTests
{
    private const string NotRegistered = "PRAG1641";
    private const string LifetimeMismatch = "PRAG1642";

    /// <summary>
    ///     The DI abstractions are not optional here: the sibling is compiled with the generator, and its
    ///     generated registration names <c>IServiceCollection</c>. A reference list that leaves it out
    ///     fails in the referenced assembly, before the assertion is reached.
    /// </summary>
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<ServiceAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>(),
        GeneratorTestHelper.FromType<global::Microsoft.Extensions.DependencyInjection.IServiceCollection>()
    ];

    /// <summary>A module of its own: one contract, registered by its <c>[Service]</c>.</summary>
    private const string SiblingModule = """
        using Pragmatic.Composition.Attributes;

        namespace Sibling.Contracts;

        public interface ISiblingReads
        {
            int Count();
        }

        [Service]
        public class SiblingReads : ISiblingReads
        {
            public int Count() => 0;
        }
        """;

    private static string ConsumerOf(string dependency) => $$"""
        using Pragmatic.Composition.Attributes;

        namespace TestApp;

        public interface ISweep { }

        [Service]
        public class Sweep : ISweep
        {
            public Sweep({{dependency}} dependency) { }
        }
        """;

    /// <summary>
    ///     The half that crosses the assembly boundary. The sibling is compiled <em>through the
    ///     generator</em>, so what the consumer sees is the metadata a real module ships, not a stub.
    /// </summary>
    [Fact]
    public void AContractASiblingModuleRegisters_IsNotReportedAsUnregistered()
    {
        var sibling = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Sibling", SiblingModule, References);

        var ids = IdsOf(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            ConsumerOf("global::Sibling.Contracts.ISiblingReads"), [.. References, sibling]));

        ids.Should().NotContain(NotRegistered,
            "the sibling's DI metadata says it registers the contract, and modules composing into one "
            + "host is the architecture — not an edge case");
    }

    /// <summary>
    ///     The control, and the reason this is not "stop checking": a contract nobody registers anywhere
    ///     is still refused. Without it the test above is satisfied by never reporting.
    /// </summary>
    [Fact]
    public void AContractNobodyRegisters_IsStillReported()
        => IdsOf(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
                ConsumerOf("global::TestApp.INothingRegistersThis"), References))
            .Should().Contain(NotRegistered);

    /// <summary>
    ///     What the list could say and a bare attribute could not: the lifetime. A singleton holding a
    ///     per-request contract is the captive dependency the well-known list was checking for, so moving
    ///     a name out of the list without carrying its lifetime would trade one hole for another.
    /// </summary>
    [Fact]
    public void ASingletonCapturingAScopedContractTheHostProvides_IsReported()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            [ProvidedByHost(Lifetime.Scoped)]
            public interface ICurrentRequest { }

            public interface ISweep { }

            [Service(Lifetime = Lifetime.Singleton)]
            public class Sweep : ISweep
            {
                public Sweep(ICurrentRequest request) { }
            }
            """;

        var ids = IdsOf(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References));

        ids.Should().Contain(LifetimeMismatch,
            "one instance would hold the first request's for every request after it");
        ids.Should().NotContain(NotRegistered, "the contract says who registers it");
    }

    /// <summary>The control for the one above: a singleton contract is not a captive dependency.</summary>
    [Fact]
    public void ASingletonCapturingASingletonContractTheHostProvides_IsNotReported()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            [ProvidedByHost(Lifetime.Singleton)]
            public interface IFileStore { }

            public interface ISweep { }

            [Service(Lifetime = Lifetime.Singleton)]
            public class Sweep : ISweep
            {
                public Sweep(IFileStore store) { }
            }
            """;

        var ids = IdsOf(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References));

        ids.Should().NotContain(LifetimeMismatch);
        ids.Should().NotContain(NotRegistered);
    }

    /// <summary>
    ///     A <c>[Published]</c> query's read contract is generated, so it cannot carry a hand-written
    ///     attribute — the generator writes the declaration onto it. <c>AddScoped</c> is what the
    ///     generated registration calls, so Scoped is what it declares.
    /// </summary>
    [Fact]
    public void APublishedReadContract_DeclaresThatSomethingAboveRegistersIt()
    {
        var source = """
            using System;
            using System.Linq;
            using System.Linq.Expressions;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;

            namespace App.Registry.Queries;

            [Entity]
            public partial class Customer : IEntity
            {
                public string Name { get; set; } = "";
            }

            public class CustomerDto
            {
                public string Name { get; set; } = "";

                public static Expression<Func<Customer, CustomerDto>> Projection =>
                    c => new CustomerDto { Name = c.Name };
            }

            [Published]
            [Query<Customer, CustomerDto>]
            public partial class GetCustomerQuery;
            """;

        var contract = GeneratorTestHelper.GetGeneratedSource(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
                source, [.. References, GeneratorTestHelper.FromType<PublishedAttribute>()]),
            "_ReadContract");

        contract.Should().NotBeNull("the query carries [Published], so a read contract is generated");
        contract!.Should().Contain("ProvidedByHost",
            "the consuming module's generator cannot see AddRegistryReads, and reported the contract it "
            + "was handed as unregistered");
    }

    private static string[] IdsOf(SourceGenRunResult result)
        => [.. result.Diagnostics.Select(d => d.Id)];
}
