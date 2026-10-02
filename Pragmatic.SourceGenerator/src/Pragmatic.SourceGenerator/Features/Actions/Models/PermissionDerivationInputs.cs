using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     What the auto-derivation posture needs to name an operation's permission: the boundaries it can
///     belong to, the catalog an <c>[ExplicitPermission]</c> constant resolves against, and whether the
///     switch is on at all.
/// </summary>
/// <remarks>
///     One value, built once in <c>ActionsFeature</c> and handed to every feature that derives — Actions
///     for its own models, Endpoints for a <c>[Query]</c>, which has no action model and is protected
///     by its route alone. ⚠️ Without the second consumer, derivation would run over actions and
///     mutations only, and a query under the posture would require nothing.
/// </remarks>
internal sealed record PermissionDerivationInputs(
    EquatableArray<BoundaryModel> Boundaries,
    EquatableArray<PermissionConstEntry> Catalog,
    bool Enabled);
