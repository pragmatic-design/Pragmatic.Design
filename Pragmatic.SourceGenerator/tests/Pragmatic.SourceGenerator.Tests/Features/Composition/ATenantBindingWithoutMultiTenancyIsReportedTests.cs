using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     PRAG1615 (#741): an operation that takes the request's tenant needs a host that resolves one, and a
///     host with no <c>Pragmatic.MultiTenancy</c> is told at build time.
/// </summary>
/// <remarks>
///     The shape of PRAG1698 for the clock: <c>[FromTenant]</c> and <c>ITenantContext</c> are in
///     Abstractions, which every module has, and the resolution is in a package only the host references.
///     Without it the invoker reads no tenant on every call.
/// </remarks>
public class ATenantBindingWithoutMultiTenancyIsReportedTests
{
    private const string Diagnostic = "PRAG1615";

    /// <summary>The attribute is declared in source: the generator matches it by full name.</summary>
    private const string Stubs = """
        using System;

        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        namespace Pragmatic.MultiTenancy
        {
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class FromTenantAttribute : Attribute { }
        }
        namespace Pragmatic.Actions.Attributes
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class DomainActionAttribute : Attribute { }
        }
        public static class Program
        {
            public static void Main() { }
        }
        """;

    /// <summary>The type the feature detector reads multi-tenancy from.</summary>
    private const string MultiTenancyPresence = """

        namespace Pragmatic.MultiTenancy
        {
            public class InMemoryTenantStore { }
        }
        """;

    private const string Operation = """

        namespace TestApp
        {
            using Pragmatic.Actions.Attributes;
            using Pragmatic.MultiTenancy;

            [DomainAction]
            public partial class ReadOwnCompanyAction
            {
                [FromTenant]
                public string Tenant { get; private set; } = "";
            }
        }
        """;

    [Fact]
    public void AHostWithNoMultiTenancy_IsToldAtBuildTime()
        => IdsOf(RunAsHost(Stubs + Operation)).Should().Contain(Diagnostic);

    /// <summary>The control: with multi-tenancy there, nothing is said.</summary>
    [Fact]
    public void AHostThatResolvesATenant_IsNotReported()
        => IdsOf(RunAsHost(Stubs + MultiTenancyPresence + Operation)).Should().NotContain(Diagnostic);

    /// <summary>The second control: no [FromTenant] anywhere, no report.</summary>
    [Fact]
    public void AHostWithNothingAskingForATenant_IsNotReported()
        => IdsOf(RunAsHost(Stubs)).Should().NotContain(Diagnostic);

    /// <summary>A module says it takes the tenant, so the host that references it can answer for it.</summary>
    [Fact]
    public void AModuleThatTakesTheTenant_SaysSoInItsMetadata()
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + Operation, []));

        sources.Keys.Should().Contain(k => k.Contains("_Metadata.TenantBindings"));
    }

    private static SourceGenRunResult RunAsHost(string source)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(source, []);

    private static string[] IdsOf(SourceGenRunResult result)
        => [.. result.Diagnostics.Select(d => d.Id)];
}
