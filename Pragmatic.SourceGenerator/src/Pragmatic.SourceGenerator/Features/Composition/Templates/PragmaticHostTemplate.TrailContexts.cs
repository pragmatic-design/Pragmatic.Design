// Pragmatic.SourceGenerator - Composition - The audit trail's and the subject registry's contexts

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Registers <c>AuditDbContext</c> and <c>PrivacyDbContext</c> on the database whose migration creates
///     their tables, with the services that read and write them.
/// </summary>
/// <remarks>
///     <para>
///         Every application with an <c>[Audited]</c> entity or a <c>[DataSubject]</c> wrote the same lines
///         in <c>Program.cs</c> — <c>AddDbContext&lt;AuditDbContext&gt;(o =&gt; o.UseNpgsql(conn("App")))</c>
///         and <c>AddAuditTrail()</c>, the same for the registry — naming a database, a provider and a
///         connection key the host already declares. The tables are created here too: the boundary
///         context of that database maps them.
///     </para>
///     <para>
///         ⚠️ An application that registered the context itself keeps it, and with it the wiring: the
///         check runs after the <c>configure</c> callback, so it sees the application's registration.
///         Two <c>AddDbContext</c> calls would both configure the options, and a different provider in
///         the second is an EF Core error at the first query.
///     </para>
/// </remarks>
internal sealed partial class PragmaticHostTemplate
{
    /// <summary>States, once, the case the host cannot decide: the trail's tables in several databases.</summary>
    private void RenderAuditTrailAmbiguity()
    {
        if (!_model.DetectedFeatures.HasAuditEFCore || _model.PersistedStores.AuditedDatabases.Count < 2)
            return;

        Comment("[Audited] entities live in more than one database, and AuditDbContext reads one: register it");
        Comment("in Program.cs, with AddAuditTrail(), against the database the trail should be read from.");
        AppendLine();
    }

    /// <summary>The trail and the registry, when this is the database that holds their tables.</summary>
    private void RenderTrailContexts(
        string databaseTypeName, string? provider, string? configKey, string? inMemoryRootVar, string dbSimpleName)
    {
        var features = _model.DetectedFeatures;
        var stores = _model.PersistedStores;
        var useMethod = DatabaseProviderCall.UseMethod(provider);

        if (features.HasAuditEFCore && stores.AuditedDatabases.Count == 1 && stores.IsAuditedDatabase(databaseTypeName))
        {
            // Without the retry: the trail enlists in the transaction the change began, and a retrying
            // strategy refuses to run inside a transaction it did not start.
            var arguments = provider == "InMemory"
                ? BuildProviderArgLine(provider, configKey, inMemoryRootVar, dbSimpleName)
                : DatabaseProviderCall.Arguments(provider,
                    string.IsNullOrEmpty(configKey) ? null : $"configuration[\"{configKey}\"]", configKey, retry: false);

            Comment($"Audit trail — [Audited] entities live in {dbSimpleName}, whose migration creates its tables");
            RenderUnlessRegistered("global::Pragmatic.Audit.EFCore.AuditDbContext", useMethod, arguments,
                "global::Pragmatic.Audit.EFCore.AuditEfCoreExtensions.AddAuditTrail(services);");
        }

        if (features.HasPrivacyEFCore && stores.SubjectDatabases.Count == 1 && stores.IsSubjectDatabase(databaseTypeName))
        {
            Comment($"Subject registry — the [DataSubject] entities live in {dbSimpleName}, whose migration creates its tables.");
            Comment("Its keys (ISecretEncryptor, ISubjectLookupKeyProvider) are the application's to register.");
            RenderUnlessRegistered("global::Pragmatic.Privacy.EFCore.PrivacyDbContext", useMethod,
                BuildProviderArgLine(provider, configKey, inMemoryRootVar, dbSimpleName),
                "global::Pragmatic.Privacy.EFCore.PrivacyEfCoreExtensions.AddSubjectRegistry(services);");

            // The per-subject keys go with the subjects, on the same database and the same rule: the
            // generated context maps __SubjectKeys beside the registry's tables, so one migration
            // creates the person, their pseudonym and the key that opens what is protected about them.
            //
            // ⚠️ Not registering it is not a degraded mode: EfCoreSubjectKeyStore takes this context,
            // so the application starts and every request that touches a protected column dies on
            // "Unable to resolve service for type CryptographyDbContext".
            if (features.HasCryptographyEFCore)
            {
                Comment($"Per-subject keys — the ProtectedValue columns of {dbSimpleName} are opened by keys kept there.");
                RenderUnlessRegistered("global::Pragmatic.Cryptography.EFCore.CryptographyDbContext", useMethod,
                    BuildProviderArgLine(provider, configKey, inMemoryRootVar, dbSimpleName),
                    "global::Pragmatic.Cryptography.EFCore.CryptographyEfCoreExtensions.AddCryptographySubjectKeys(services);");
            }
        }
    }

    private void RenderUnlessRegistered(string contextType, string useMethod, string arguments, string wiring)
    {
        AppendLine($"if (!global::System.Linq.Enumerable.Any(services, d => d.ServiceType == typeof({contextType})))");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"services.AddDbContext<{contextType}>(options => options.{useMethod}({arguments}));");
        AppendLine(wiring);
        DecreaseIndent();
        AppendLine("}");
    }
}
