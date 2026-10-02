// Pragmatic.Composition.HostWiring.Tests - [UndoWith<T>] reaches the container
// Asserts on the generated host, not on the module's registration extension: nothing calls that
// extension, which is exactly how the first version of this feature shipped inert.

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A compensator declared with <c>[UndoWith&lt;T&gt;]</c> in a referenced library must be registered
///     by the host, together with the request scope that holds its undos.
/// </summary>
/// <remarks>
///     <para>
///         The first version emitted both into the module's <c>_Infra.Actions.Registration.g.cs</c> —
///         the file that carries <c>AddProbeActions(this IServiceCollection)</c>. No application calls
///         it: the host reads the metadata channel and rebuilds the whole registration list itself. So
///         the compensator resolved to nothing, <c>ICompensationScope</c> was absent, and every
///         compensation branch in every invoker took the null path. The mechanism was inert, and the
///         generator tests were all green — they asserted on the module's file.
///     </para>
///     <para>
///         Hence the assertion here, on <c>Host.Services.g.cs</c>: it is the only file a running
///         application executes. Same lesson as the cache category next door, and the same shape as the
///         redaction map before it.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class CompensatorWiringTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    [Fact]
    public void UndoWith_CompensatorDeclaredInAReferencedLibrary_IsRegisteredByTheHost()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices);

        lines.Should().Contain(
            "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<global::Probe.Domain.ProbeUndo>(services);",
            "an unregistered compensator makes GetRequiredService throw inside the undo path — at the "
            + $"worst possible moment. Host.Services.g.cs has {lines.Count} lines and no such registration");
    }

    /// <summary>
    ///     Without the scope every invoker's compensation branch resolves null and does nothing, which
    ///     looks exactly like an application that declared no compensator.
    /// </summary>
    [Fact]
    public void UndoWith_TheRequestScopeIsRegisteredToo()
        => HostWiringFixture.LinesOf(fixture.Control, HostServices)
            .Should().Contain(
                "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<global::Pragmatic.Actions.Compensation.ICompensationScope, global::Pragmatic.Actions.Compensation.CompensationScope>(services);",
                "the undos have nowhere to be registered without it, so the compensation silently never runs");

    /// <summary>
    ///     The generated host must compile. Asserting on the text of a line does not prove that: the
    ///     first version of this registration emitted <c>services.TryAddScoped&lt;…&gt;()</c>, which
    ///     reads correctly, matched the assertion above, and failed with CS1061 in the one place it
    ///     mattered — the host does not import the extension's namespace.
    /// </summary>
    [Fact]
    public void TheGeneratedHostCompiles()
        => fixture.ControlErrors.Should().BeEmpty(
            "a generated host that does not compile is not wiring, it is a build break: "
            + string.Join(" | ", fixture.ControlErrors.Take(5)));
}
