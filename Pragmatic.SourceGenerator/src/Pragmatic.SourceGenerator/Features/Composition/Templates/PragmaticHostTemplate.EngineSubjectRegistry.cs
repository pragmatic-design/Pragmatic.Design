// Pragmatic.SourceGenerator - Composition - The subject registry on the Sql and documents engines

using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Registers the subject registry and the consent store on the engine boundary that holds the <c>[DataSubject]</c> entities, by the rule
///     EF Core's registry follows (<c>RenderTrailContexts</c>) and the audit trail's on the engines (<c>RenderRegisterAuditTrailMethod</c>): one
///     place, and they are registered there; more than one, and the application names it in <c>Program.cs</c>.
/// </summary>
/// <remarks>
///     ⚠️ Not the per-subject keys EF Core registers with the registry (<c>AddCryptographySubjectKeys</c>): the engines do not serve a
///     <c>ProtectedValue</c> yet (#431).
/// </remarks>
internal sealed partial class PragmaticHostTemplate
{
    private const string SqlSubjectRegistry = "global::Pragmatic.Privacy.Engines.Sql.SqlPrivacyExtensions.AddSqlSubjectRegistry";
    private const string DocSubjectRegistry = "global::Pragmatic.Privacy.Engines.Documents.DocPrivacyExtensions.AddDocSubjectRegistry";

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
                AppendLine($"{(only.Engine == Core.PersistenceEngineMarker.Sql ? SqlSubjectRegistry : DocSubjectRegistry)}<{only.BoundaryType}>(services);");
            }
            else
            {
                Comment("[DataSubject] entities live in more than one place, and the registry is one: register it in Program.cs —");
                Comment("AddSqlSubjectRegistry<TBoundary>() or AddDocSubjectRegistry<TBoundary>() for an engine's boundary, AddSubjectRegistry() for EF Core's.");
            }

            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }
}
