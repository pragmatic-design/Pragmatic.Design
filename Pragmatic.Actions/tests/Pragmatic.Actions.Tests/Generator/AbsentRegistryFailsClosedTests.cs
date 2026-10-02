using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     "Nothing declared" and "the generator never ran" produce different artifacts: an empty
///     registry, and none.
///     <para>
///         If the registries were emitted only when at least one action declared a permission or a
///         policy, an assembly that declared none would produce no registry, and so would an assembly
///         whose generator output was discarded — which Roslyn does behind a CS8785 <i>warning</i> when
///         two hint names collide, leaving the build green. The runtime could not tell the two apart,
///         and an empty fallback answering "no requirement" would let every <c>[RequirePermission]</c>
///         pass.
///     </para>
///     <para>
///         So the distinction is made where the information still exists. A registry is emitted for
///         every assembly that has actions, empty when nothing is declared, so an absent registry has
///         exactly one cause and the runtime fails closed on it
///         (<c>UnavailablePermissionRequirementRegistry</c>).
///     </para>
/// </summary>
public class AbsentRegistryFailsClosedTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Result;

        namespace TestApp.Billing;

        [Boundary]
        public partial class BillingBoundary;

        [DomainAction]
        public partial class ListInvoicesAction : DomainAction<Guid>
        {
            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    /// <summary>
    ///     The load-bearing one. Revert the guard in <c>GeneratePermissionRequirementRegistry</c> to
    ///     the old <c>entries.IsEmpty</c> and this goes red: an assembly with actions and no
    ///     declarations emits nothing again, and absence stops being a signal.
    /// </summary>
    [Fact]
    public void AnAssemblyWithActionsAndNoPermissions_StillEmitsAPermissionRegistry()
    {
        var result = RunGenerator(Source);

        GetGeneratedSource(result, "PermissionRequirementRegistry").Should().NotBeNull();
    }

    [Fact]
    public void AnAssemblyWithActionsAndNoPolicies_StillEmitsAPolicyRegistry()
    {
        var result = RunGenerator(Source);

        GetGeneratedSource(result, "PolicyRegistry").Should().NotBeNull();
    }

    /// <summary>
    ///     The emitted registry is genuinely empty rather than accidentally populated — otherwise the
    ///     test above would pass for the wrong reason, on a registry that happens to contain
    ///     something.
    /// </summary>
    [Fact]
    public void ThatRegistryKnowsNoAction()
    {
        var result = RunGenerator(Source);

        GetGeneratedSource(result, "PermissionRequirementRegistry")
            .Should().NotBeNull().And.NotContain("ListInvoicesAction");
    }

    /// <summary>
    ///     And the registration still points at it, so the empty generated registry — not the
    ///     fail-closed fallback — is what the container resolves.
    /// </summary>
    [Fact]
    public void TheGeneratedRegistrationStillRegistersIt()
    {
        var result = RunGenerator(Source);

        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(s => s.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration!.Should().Contain("GeneratedPermissionRequirementRegistry");
    }
}
