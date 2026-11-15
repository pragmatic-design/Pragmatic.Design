namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     An entity's <c>[TenantSource]</c>, its members checked: what the generated <c>{Entity}.TenantStore</c> reads a tenant from.
/// </summary>
/// <param name="Id">The <c>string</c> property holding the tenant id.</param>
/// <param name="Name">The <c>string</c> property holding the tenant's name.</param>
/// <param name="State">The <c>TenantState</c> property.</param>
/// <param name="CreatedAt">The <c>DateTimeOffset</c> property holding when it was registered; null when there is none.</param>
/// <param name="CreatedAtNullable">That property is a <c>DateTimeOffset?</c>.</param>
internal sealed record TenantSourceModel(string Id, string Name, string State, string? CreatedAt, bool CreatedAtNullable);
