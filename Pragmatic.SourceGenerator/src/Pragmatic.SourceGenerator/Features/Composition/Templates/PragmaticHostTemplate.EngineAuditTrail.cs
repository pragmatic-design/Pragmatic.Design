// Pragmatic.SourceGenerator - Composition - The audit trail on the Sql and documents engines

using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Registers the audit trail's store on the engine boundary whose saves write its entries, by the rule EF Core's trail follows
///     (<c>RenderTrailContexts</c>): one place holds the <c>[Audited]</c> entities, and the trail is read there; more than one, and the
///     application names it in <c>Program.cs</c>.
/// </summary>
/// <remarks>
///     <para>
///         The boundary's session is the application's to register (<c>Add{Boundary}Persistence</c>), and the store resolves it when it is
///         first asked for, so the registration does not depend on the order of the two.
///     </para>
///     <para>
///         ⚠️ Called after the <c>configure</c> callback, as <c>RegisterAllDatabases</c> is: the store's registration is <c>TryAdd</c>, so a
///         trail the application registered there is the one it keeps.
///     </para>
/// </remarks>
internal sealed partial class PragmaticHostTemplate
{
    private const string SqlAuditTrail = "global::Pragmatic.Audit.Engines.Sql.SqlAuditExtensions.AddSqlAuditTrail";
    private const string DocAuditTrail = "global::Pragmatic.Audit.Engines.Documents.DocAuditExtensions.AddDocAuditTrail";

    private void RenderRegisterAuditTrailMethod()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        XmlSummary("Registers the audit trail on the engine boundary that holds the [Audited] entities, when it is the only place that does.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter> { new("this IServiceCollection", "services") };

        Method("RegisterAuditTrail", () =>
        {
            var stores = _model.PersistedStores;
            var boundaries = stores.AuditedEngineBoundaries.AsImmutableArray();
            if (stores.AuditTrailPlaces(_model.DetectedFeatures.HasAuditEFCore) == 1)
            {
                var only = boundaries[0];
                var name = GetSimpleName(only.BoundaryType);
                Comment($"Audit trail — [Audited] entities live in {name} only ({only.Engine} engine): the trail is read through its session");
                AppendLine($"{(only.Engine == Core.PersistenceEngineMarker.Sql ? SqlAuditTrail : DocAuditTrail)}<{only.BoundaryType}>(services);");
            }
            else
            {
                Comment("[Audited] entities live in more than one place, and the trail is read from one: register it in Program.cs —");
                Comment("AddSqlAuditTrail<TBoundary>() or AddDocAuditTrail<TBoundary>() for an engine's boundary, AddAuditTrail() for EF Core's.");
            }

            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }
}
