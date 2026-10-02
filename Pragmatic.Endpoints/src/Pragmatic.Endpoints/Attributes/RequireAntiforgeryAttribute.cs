namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Requires antiforgery token validation for this endpoint (browser form/cookie scenarios).
/// </summary>
/// <remarks>
///     The host must have the antiforgery services and middleware active — with Pragmatic
///     Composition this is wired automatically (AntiforgeryStep); in library mode call
///     <c>AddAntiforgery()</c> and <c>UseAntiforgery()</c> yourself. On form endpoints this
///     attribute replaces the default <c>DisableAntiforgery()</c> emission.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequireAntiforgeryAttribute : Attribute;
