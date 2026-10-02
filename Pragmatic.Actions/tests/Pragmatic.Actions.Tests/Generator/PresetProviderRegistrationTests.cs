using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[PresetProvider&lt;T&gt;]</c>: the provider the generated invoker resolves is registered for it.
/// </summary>
/// <remarks>
///     <para>
///         The invoker resolves each provider with <c>GetRequiredService</c>. Without a registration,
///         declaring the attribute would make <b>every</b> creation of that entity answer 500 with
///         "No service for type … has been registered" — not only one that wanted a preset, and with no
///         diagnostic pointing at the missing line.
///     </para>
///     <para>
///         ⚠️ Presets run on create, so only an application that creates the entity reaches this
///         branch; one whose mutations over a <c>[HasPresets]</c> entity are all
///         <c>MutationMode.Update</c> never executes it.
///     </para>
/// </remarks>
public class PresetProviderRegistrationTests : ActionsGeneratorTestBase
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Lifecycle;
        using Pragmatic.Result;

        namespace TestApp.Knowledge;

        [Boundary]
        public partial class KnowledgeBoundary;

        public sealed class FirstStewardPreset : IPresetProvider<Term>
        {
            public Task<IReadOnlyList<object>> CreatePresetsAsync(
                Term parent, LifecycleContext context, CancellationToken ct)
                => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        }
        """;

    private static string Source(string entityAttributes) => $$"""
        {{Preamble}}

        [Entity]
        [BelongsTo<KnowledgeBoundary>]
        {{entityAttributes}}
        public partial class Term : IEntity
        {
            public Guid PersistenceId { get; set; }
            public string Name { get; set; } = "";
        }

        [Mutation(Mode = MutationMode.Create)]
        [BelongsTo<KnowledgeBoundary>]
        public partial class DeclareTermMutation : Mutation<Term>
        {
            public required string Name { get; init; }
        }
        """;

    /// <summary>The provider the invoker asks the container for is registered by the same generator.</summary>
    [Fact]
    public void PresetProvider_IsRegisteredAlongsideTheMutationThatResolvesIt()
    {
        var result = RunGeneratorWithEntities(
            Source("[HasPresets]\n[PresetProvider<FirstStewardPreset>]"));

        var registration = GetGeneratedSource(result, "Mutations.Registration")!;

        registration.Should().Contain("FirstStewardPreset",
            "the invoker resolves it with GetRequiredService, so something has to put it there");

        // TryAdd, not Add: two mutations on the same entity would otherwise register it twice, and an
        // application wanting a different lifetime must be able to register it first and win.
        registration.Should().Contain("TryAddScoped",
            "a second mutation on the same entity must not register the provider twice");
    }

    /// <summary>⚠️ The control: no preset declared, nothing registered.</summary>
    /// <remarks>
    ///     Without this the assertion above would hold just as well on a generator that registered every
    ///     type it could see, which would quietly change the container for applications declaring no
    ///     presets at all.
    /// </remarks>
    [Fact]
    public void WithoutThePresetAttributes_NothingIsRegisteredForIt()
    {
        var result = RunGeneratorWithEntities(Source(""));

        GetGeneratedSource(result, "Mutations.Registration")
            .Should().NotContain("FirstStewardPreset",
                "an entity with no presets declared gets no preset registration");
    }
}
