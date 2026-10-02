using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     What the invoker of an action or a mutation writes before the body from who is calling and when:
///     the <c>[FromClock]</c> and <c>[FromCurrentUser]</c> properties, read as a query's are, and the
///     <c>[LoadCurrentUser]</c> field.
/// </summary>
/// <remarks>
///     The two binding models are the query's own: the attributes mean the same thing on every
///     operation, so a second model for the same declaration would be a second place for the rules to
///     drift apart in. The user load sits beside them because it reaches the same entity the same way.
/// </remarks>
internal sealed record InvokerBindingsModel
{
    /// <summary>An operation that binds nothing.</summary>
    public static readonly InvokerBindingsModel None = new();

    /// <summary>The <c>[FromClock]</c> properties.</summary>
    public EquatableArray<ClockBindingModel> Clock { get; init; } = EquatableArray<ClockBindingModel>.Empty;

    /// <summary>The <c>[FromCurrentUser]</c> properties, resolved against the user entity once it has arrived.</summary>
    public EquatableArray<CurrentUserBindingModel> CurrentUser { get; init; } =
        EquatableArray<CurrentUserBindingModel>.Empty;

    /// <summary><c>[LoadCurrentUser]</c>, or null when the operation does not declare it.</summary>
    public CurrentUserLoadModel? CurrentUserLoad { get; init; }

    /// <summary>Whether the invoker writes anything, which is what the preparation hook exists for.</summary>
    public bool AnyRendered => Clock.Any(b => b.IsRendered) || CurrentUser.Any(b => b.IsRendered) || LoadsTheUser;

    /// <summary>Whether the signed-in user's entity is loaded into the operation.</summary>
    public bool LoadsTheUser => CurrentUserLoad is { IsRendered: true };

    /// <summary>Whether writing them reads the user entity: the one thing here that awaits.</summary>
    public bool ReadsTheUser =>
        CurrentUser.Any(b => b.IsRendered && b.ResolverTypeFullName is not null) || LoadsTheUser;
}
