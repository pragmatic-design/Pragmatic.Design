namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     Framework-reserved property names that must never be exposed to client-driven grid
///     filter/sort surfaces. Two independent grid paths auto-derive their field list from the
///     entity's scalar properties — <c>[GridAdapter&lt;T&gt;]</c> (<see cref="Transforms.GridAdapterTransform"/>)
///     and <c>[GenerateGridBridge]</c> (<see cref="Transforms.GridFilterBridgeTransform"/>). Without a
///     guard a client can target a credential column (e.g. <c>PasswordHash</c>) with
///     <c>startswith</c>/<c>contains</c> and use the presence/absence of rows as a boolean oracle to
///     exfiltrate the value character-by-character, or read the authorization shape
///     (<c>OwnerId</c>/<c>AccessScopes</c>) to plan a privilege escalation.
/// </summary>
/// <remarks>
///     The names live in <c>Pragmatic.Contracts.SensitiveGridFieldNames</c>, linked as source into
///     this generator and into <c>Pragmatic.Persistence</c>, whose <c>AdapterFieldPolicy</c> guards the
///     runtime PrimeNG/DevExpress adapters. ⚠️ One list, not two hand-maintained copies: a plain static
///     class of strings compiles in both netstandard2.0 and net10.0, as <c>ReservedWireNames</c> does
///     for the other list. What remains here is the name this feature calls it by.
/// </remarks>
internal static class SensitiveFieldNames
{
    /// <summary>
    ///     True when <paramref name="propertyName"/> is a framework-reserved sensitive column that
    ///     must be withheld from client-driven grid exposure (matched case-insensitively).
    /// </summary>
    public static bool IsSensitive(string propertyName)
        => global::Pragmatic.Contracts.SensitiveGridFieldNames.IsSensitive(propertyName);
}
