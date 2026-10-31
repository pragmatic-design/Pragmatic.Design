// Pragmatic.SourceGenerator - Composition - The entry point's call to the engines' audit trail

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

internal sealed partial class PragmaticEntryTemplate
{
    /// <summary>
    ///     The call to <c>PragmaticHost.RegisterAuditTrail</c>, after the <c>configure</c> callback: a trail the application registered there
    ///     is the one it keeps, since the store's registration is <c>TryAdd</c>.
    /// </summary>
    private void RenderAuditTrailRegistration()
    {
        if (!_model.HasEngineAuditTrail)
            return;

        Comment("Register the audit trail on the engine boundary that holds the [Audited] entities");
        AppendLine($"global::{_model.RootNamespace}.PragmaticHost.RegisterAuditTrail(builder.Services);");
        AppendLine();
    }
}
