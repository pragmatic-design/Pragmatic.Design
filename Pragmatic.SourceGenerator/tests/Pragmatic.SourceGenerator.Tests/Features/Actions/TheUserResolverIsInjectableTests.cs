using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Composition.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     An operation can declare a field typed as the resolver of the module's <c>[PragmaticUser]</c>
///     entity, and the generated invoker receives it.
/// </summary>
/// <remarks>
///     <para>
///         The same position as <see cref="TheOwnBoundaryInterfaceIsInjectableTests" />: the resolver is
///         written by this generator, later in the same compilation, so while the field is classified its
///         type is an error type — no interface, no attributes, nothing but the name. Left there it is
///         reported as unclassifiable (PRAG0419) and not injected, which is what Time off hit the moment it
///         replaced its hand-written <c>CurrentEmployee</c> with the generated resolver.
///     </para>
///     <para>
///         ⚠️ The control keeps it narrow: the name is recognised because a <c>[PragmaticUser]</c> entity of
///         this compilation produces it, not because it ends in <c>Resolver</c>.
///     </para>
/// </remarks>
public class TheUserResolverIsInjectableTests
{
    private const string Domain = """
        namespace Pragmatic.Identity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticUserAttribute : System.Attribute
            {
                public string MatchClaim { get; set; } = "sub";
                public string? MatchProperty { get; set; }
            }
        }

        namespace Contoso.Billing
        {
            [Pragmatic.Composition.Attributes.Module]
            public sealed class BillingModule;

            [Pragmatic.Persistence.Entity.Entity]
            [Pragmatic.Identity.PragmaticUser(MatchProperty = "UserKey")]
            public partial class Clerk : Pragmatic.Persistence.Entity.IEntity
            {
                public string UserKey { get; private set; } = "";
            }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromType<EndpointAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Mutation<>))
    ];

    private static SourceGenRunResult Run(string fieldType) => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
        Domain + $$"""

        namespace Contoso.Billing.Invoices.Actions
        {
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;

            [DomainAction]
            public partial class ReissueInvoiceAction : VoidDomainAction
            {
                private {{fieldType}} _clerks = null!;
            }
        }
        """,
        References);

    [Fact]
    public void TheUserResolver_IsInjected_AndNotReportedAmbiguous()
    {
        // The namespace the action is in is not the entity's: the name has to be qualified by whoever
        // recognised it, or the invoker binds only where a using happens to bring it in.
        var result = Run("Contoso.Billing.ClerkResolver");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0419").Should().BeFalse(
            "this compilation writes the resolver of its [PragmaticUser] entity, so the name is a service");

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(f => f.Key.Contains("ReissueInvoiceAction.Invoker")).Value
            .Should().Contain("global::Contoso.Billing.ClerkResolver",
                "the constructor has to actually receive it, qualified");
    }

    /// <summary>The control: a resolver name no [PragmaticUser] entity here produces stays ambiguous.</summary>
    [Fact]
    public void AResolverNameNoUserEntityProduces_IsStillReportedAmbiguous()
    {
        var result = Run("Contoso.Billing.AuditorResolver");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0419").Should().BeTrue(
            "no user entity named Auditor exists, so nothing emits that resolver");
    }
}
