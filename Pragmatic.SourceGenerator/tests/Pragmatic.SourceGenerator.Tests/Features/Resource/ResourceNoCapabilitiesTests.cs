using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
///     PRAG2612 — a <c>[Resource]</c> that scaffolds nothing.
/// </summary>
/// <remarks>
///     <para>
///         <c>Capabilities</c> defaults to <c>None</c> and the feature skips a resource that has
///         none: no DTO, no action, no endpoint, and nothing said. A consumer added the attribute,
///         looked for a route, and found neither the route nor a reason.
///     </para>
///     <para>
///         The Showcase had the same case. <c>Reservation</c> carried <c>[Resource("reservations")]</c>
///         beside a summary reading "read-only: create/update managed by domain actions" — an intent
///         that produced nothing at all, for the two years the attribute sat there.
///     </para>
/// </remarks>
public class ResourceNoCapabilitiesTests
{
    private const string Shims = """
        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]

            [System.Flags]
            public enum ResourceCapabilities
            {
                None = 0, Create = 1, Read = 2, Update = 4,
                Delete = 8, List = 16, Search = 32, Restore = 64
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ResourceAttribute : System.Attribute
            {
                public ResourceAttribute(string segment) { Segment = segment; }
                public string Segment { get; }
                public ResourceCapabilities Capabilities { get; set; } = ResourceCapabilities.None;
            }
        }

        namespace Pragmatic.Domain
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }
        }

        namespace Pragmatic.Actions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class BoundaryAttribute : System.Attribute { }
        }

        namespace Pragmatic.Tags
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class HasTagsAttribute : System.Attribute { }
        }
        """;

    private static string Entity(string attributes) => Shims + $$"""

        namespace Sales
        {
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Domain;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Tags;

            [Boundary]
            public partial class SalesBoundary { }

            [Entity]
            {{attributes}}
            public partial class Order { public string Code { get; set; } = ""; }
        }
        """;

    [Fact]
    public void ResourceWithNoCapabilities_ReportsPrag2612()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Entity("""[Resource("orders")]"""), []);

        var diagnostics = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2612").ToList();

        diagnostics.Should().ContainSingle();
        diagnostics[0].GetMessage().Should().Contain("Order").And.Contain("orders");
    }

    /// <summary>
    ///     The control: a resource that asks for something is left alone.
    /// </summary>
    /// <remarks>
    ///     Without it the test above would also pass on a diagnostic that fired on every
    ///     <c>[Resource]</c>, which would report the working case as loudly as the broken one.
    /// </remarks>
    [Fact]
    public void ResourceWithCapabilities_ReportsNothing()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Entity("""[Resource("orders", Capabilities = ResourceCapabilities.Read)]"""), []);

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2612").Should().BeEmpty();
    }

    /// <summary>
    ///     A resource that scaffolds nothing <b>and carries a trait</b> is not the same case.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The attribute's own documentation names this use: the generator uses it "to resolve
    ///         route prefixes for traits <b>and optionally</b> auto-scaffold CRUD endpoints". An entity
    ///         with <c>[HasTags]</c> needs the segment for
    ///         <c>POST/GET/DELETE /api/{boundary}/{segment}/{id}/tags</c> and wants no CRUD; those
    ///         endpoints <em>are</em> generated, and PRAG2612 called it a mistake anyway.
    ///     </para>
    ///     <para>
    ///         The advice made it worse: "or remove the attribute" is what PRAG2601 exists to complain
    ///         about, and removing it takes the three routes with it. Between the two diagnostics there
    ///         was no way to write this entity without a warning.
    ///     </para>
    ///     <para>
    ///         The control is <c>ResourceWithNoCapabilities_ReportsPrag2612</c> above: the same source
    ///         through the same helper, differing only in the trait attribute, so this is not a
    ///         diagnostic that stopped firing.
    ///     </para>
    ///     <para>
    ///         ⚠️ The other half — that PRAG2601 still fires for a trait with no <c>[Resource]</c> —
    ///         is <b>not</b> asserted here and is not asserted anywhere: PRAG2601 has no test in this
    ///         repository, and reaching it needs the trait pipeline's full reference closure rather
    ///         than the shims above. What makes that acceptable for this change is that PRAG2601 is
    ///         emitted by <c>TraitFeature</c> and nothing here touches it.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ResourceWithNoCapabilitiesButATrait_ReportsNothing()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Entity("""
                [Resource("orders")]
                    [HasTags]
                """), []);

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG2612").Should().BeEmpty(
            "the segment is what the trait routes hang under, so the resource does publish something");
    }
}
