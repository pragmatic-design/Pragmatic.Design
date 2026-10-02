using Pragmatic.SourceGen;

namespace Pragmatic.Testing.SourceGenerator.Models;

/// <summary>
///     One transition endpoint's data for a state-transition contract test (#7): the endpoint that moves an
///     entity into <see cref="TargetState"/>, the create endpoint that produces it in <see cref="InitialState"/>,
///     and whether the initial → target move is legal (per the enum's <c>[TransitionFrom]</c> graph).
/// </summary>
/// <remarks>
///     The doc's transition row asks for two assertions: a valid transition → status changed, and an illegal
///     transition → 409. Which one is generatable without a multi-step setup flow depends on
///     <see cref="InitialToTargetIsLegal"/>: from a freshly-created entity (initial state) only the matching one
///     is reachable in a single POST. The other is emitted as a skipped placeholder rather than a false test.
/// </remarks>
internal sealed record StateTransitionModel
{
    public required string Boundary { get; init; }
    public required string EntityName { get; init; }

    /// <summary>The transition endpoint route, e.g. <c>/api/invoices/{id}/pay</c>.</summary>
    public required string TransitionRoute { get; init; }

    /// <summary>
    ///     The create endpoint route that yields the entity in its initial state, e.g. <c>/api/invoices</c>.
    ///     Empty when no correlatable create exists — see <see cref="UngeneratableReason"/>.
    /// </summary>
    public required string CreateRoute { get; init; }

    /// <summary>
    ///     Why this transition cannot be tested, or <c>null</c> when it can. Set when no create endpoint could
    ///     be correlated to the entity: the test needs the entity in its initial state before it can transition
    ///     it. The transition is still emitted, as a skipped placeholder naming this reason — a state machine
    ///     that silently produces no contract at all is worse than one that says why.
    /// </summary>
    public string? UngeneratableReason { get; init; }

    /// <summary>The permission the transition endpoint declares, or null when it declares none.</summary>
    public required string? Permission { get; init; }

    /// <summary>
    ///     The permission the <b>create</b> endpoint declares, which is usually not the transition's.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not <see cref="Permission" />. The ordinary shape is two different ones — <c>x.create</c>
    ///     and <c>x.update</c> — so sending the create with the transition's permission would get the
    ///     arrangement refused and fail the contract on the create, which says nothing about the
    ///     transition it exists to measure. Null when the create declares
    ///     none: the request then goes out as the unprivileged caller rather than borrowing an
    ///     authority nobody granted it.
    /// </remarks>
    public string? CreatePermission { get; init; }

    public required string TargetState { get; init; }
    public required string InitialState { get; init; }

    /// <summary>True when the entity's initial state can legally transition into <see cref="TargetState"/>.</summary>
    public required bool InitialToTargetIsLegal { get; init; }

    /// <summary>
    ///     The states <see cref="TargetState" /> may be entered from (its <c>[TransitionFrom]</c> set), in a
    ///     stable order: with the other transitions of the entity, what a contract walks to reach a legal
    ///     or an illegal source state when the initial one is not it.
    /// </summary>
    public EquatableArray<string> LegalSources { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The synthesized body for the create call, reused from the CRUD synthesizer.</summary>
    public required EquatableArray<CrudFieldModel> CreateFields { get; init; }

    /// <summary>
    ///     The name of the create <b>operation</b> this flow arranges through — the same key the CRUD
    ///     contract for that endpoint uses, so an application answers for it once.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not <c>"Create" + EntityName</c>: the CRUD contract asks under the endpoint's action name.
    ///     Two different keys would leave an application that wrote one with the other contract posting a
    ///     synthesised body, so the arrangement would fail and the transition would never be reached.
    /// </remarks>
    public string CreateOperation { get; init; } = "";

    /// <summary>
    ///     The body the <b>transition</b> request posts: the endpoint's own members, filled by the same
    ///     reader the create's body comes from.
    /// </summary>
    /// <remarks>
    ///     Empty for a transition that takes nothing but its route, which is the majority and which
    ///     posts no content at all. As the only behaviour, an endpoint that requires a body —
    ///     approving with a note, paying with an amount — would answer <b>415</b> before it could answer the 409 or the 2xx the contract exists to measure.
    /// </remarks>
    public EquatableArray<CrudFieldModel> TransitionFields { get; init; } =
        EquatableArray<CrudFieldModel>.Empty;

    /// <summary>
    ///     Whether the transition's body could be filled from its shape. False when a required member is
    ///     one the synthesiser cannot invent, and then the application supplies it through
    ///     <c>PragmaticContractHost.BodyFor</c> — under the transition's own operation name.
    /// </summary>
    public bool CanSynthesizeTransitionBody { get; init; } = true;

    /// <summary>
    ///     Whether the transition endpoint takes a body at all — and therefore whether the contract posts
    ///     one. The model answers it, so the two branches of the template cannot read it differently.
    /// </summary>
    /// <remarks>
    ///     A member the reader could not fill counts <b>only</b> when it is required: that is the case where
    ///     the endpoint does take a body and the application has to supply it. An optional one it could not
    ///     fill leaves the request with no content, which is what keeps this additive for every transition that only needs its route.
    /// </remarks>
    public bool TransitionTakesABody => TransitionFields.Count > 0 || !CanSynthesizeTransitionBody;
}
