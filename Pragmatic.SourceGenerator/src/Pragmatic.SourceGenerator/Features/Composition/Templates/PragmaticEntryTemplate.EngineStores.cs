// Pragmatic.SourceGenerator - Composition - The entry point's calls to the engines' module stores

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Where the entry point registers the stores of the engines' boundaries, each where an application's own registration of it wins: the two
///     registrations are opposite, so they sit on the two sides of the <c>configure</c> callback.
/// </summary>
internal sealed partial class PragmaticEntryTemplate
{
    /// <summary>
    ///     The call to <c>PragmaticHost.RegisterAuditTrail</c>, after the <c>configure</c> callback: the trail's registration is <c>TryAdd</c>, so
    ///     a trail the application registered there is the one it keeps.
    /// </summary>
    private void RenderAuditTrailRegistration()
    {
        if (!_model.HasEngineAuditTrail)
            return;

        Comment("Register the audit trail on the engine boundary that holds the [Audited] entities");
        AppendLine($"global::{_model.RootNamespace}.PragmaticHost.RegisterAuditTrail(builder.Services);");
        AppendLine();
    }

    /// <summary>
    ///     The call to <c>PragmaticHost.RegisterSubjectRegistry</c>, after the defaults and before the <c>configure</c> callback: the registry's
    ///     registration replaces <c>AddPrivacy</c>'s fallback (registered with the defaults), so it comes after them, and a registry the
    ///     application registers in <c>configure</c> replaces it in turn.
    /// </summary>
    private void RenderSubjectRegistryRegistration()
    {
        if (!_model.HasEngineSubjectRegistry)
            return;

        Comment("Register the subject registry on the engine boundary that holds the [DataSubject] entities");
        AppendLine($"global::{_model.RootNamespace}.PragmaticHost.RegisterSubjectRegistry(builder.Services);");
        AppendLine();
    }
}
