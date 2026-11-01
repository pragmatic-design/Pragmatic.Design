// Pragmatic.SourceGenerator - Composition - The subject registry on the Sql and documents engines

using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Registers the subject registry and the consent store on the engine boundary that holds the <c>[DataSubject]</c> entities, by the rule
///     EF Core's registry follows (<c>RenderTrailContexts</c>) and the audit trail's on the engines (<c>RenderRegisterAuditTrailMethod</c>): one
///     place, and they are registered there; more than one, and the application names it in <c>Program.cs</c>.
/// </summary>
/// <remarks>
///     With <c>Pragmatic.Cryptography.Engines</c> referenced, the per-subject keys go with them, on the same boundary, as EF Core's host registers
///     <c>AddCryptographySubjectKeys</c> beside its registry: the keys that open the <c>ProtectedValue</c> columns live in the subjects' database,
///     so erasing a value is destroying its key there.
/// </remarks>
internal sealed partial class PragmaticHostTemplate
{
    private const string SqlSubjectRegistry = "global::Pragmatic.Privacy.Engines.Sql.SqlPrivacyExtensions.AddSqlSubjectRegistry";
    private const string DocSubjectRegistry = "global::Pragmatic.Privacy.Engines.Documents.DocPrivacyExtensions.AddDocSubjectRegistry";
    private const string SqlSubjectKeys = "global::Pragmatic.Cryptography.Engines.Sql.SqlCryptographyExtensions.AddSqlSubjectKeys";
    private const string DocSubjectKeys = "global::Pragmatic.Cryptography.Engines.Documents.DocCryptographyExtensions.AddDocSubjectKeys";

    private void RenderRegisterSubjectRegistryMethod()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        XmlSummary("Registers the subject registry on the engine boundary that holds the [DataSubject] entities, when it is the only place that does.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter> { new("this IServiceCollection", "services") };

        Method("RegisterSubjectRegistry", () =>
        {
            var stores = _model.PersistedStores;
            if (stores.SubjectRegistryPlaces(_model.DetectedFeatures.HasPrivacyEFCore) == 1)
            {
                var only = stores.SubjectEngineBoundaries.AsImmutableArray()[0];
                Comment($"Subject registry — the [DataSubject] entities live in {GetSimpleName(only.BoundaryType)} only ({only.Engine} engine), whose database holds its tables.");
                Comment("Its keys (ISecretEncryptor, ISubjectLookupKeyProvider) are the application's to register.");
                var sql = only.Engine == Core.PersistenceEngineMarker.Sql;
                AppendLine($"{(sql ? SqlSubjectRegistry : DocSubjectRegistry)}<{only.BoundaryType}>(services);");
                if (_model.DetectedFeatures.HasCryptographyEngines)
                {
                    Comment("Per-subject keys — the ProtectedValue columns there are opened by keys kept beside the subjects. The master key ring");
                    Comment("(ISecretEncryptor) is the application's to register.");
                    AppendLine($"{(sql ? SqlSubjectKeys : DocSubjectKeys)}<{only.BoundaryType}>(services);");
                }
            }
            else
            {
                Comment("[DataSubject] entities live in more than one place, and the registry is one: register it in Program.cs —");
                Comment("AddSqlSubjectRegistry<TBoundary>() or AddDocSubjectRegistry<TBoundary>() for an engine's boundary, AddSubjectRegistry() for EF Core's;");
                Comment("and their keys beside them: AddSqlSubjectKeys<TBoundary>(), AddDocSubjectKeys<TBoundary>() or AddCryptographySubjectKeys().");
            }

            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }
}
