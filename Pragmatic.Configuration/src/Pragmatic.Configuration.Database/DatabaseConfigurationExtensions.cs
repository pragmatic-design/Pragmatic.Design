using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Configuration.Database.Audit;
using Pragmatic.Configuration.Database.Dialects;
using Pragmatic.Configuration.Database.Encryption;
using Pragmatic.Configuration.Database.Schema;
using Pragmatic.Configuration.Extensions;
using Pragmatic.Cryptography;

namespace Pragmatic.Configuration.Database;

/// <summary>
///     DI registration for the database configuration backend.
/// </summary>
public static class DatabaseConfigurationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds a database-backed configuration store. It connects with
        ///     <see cref="DatabaseConfigurationOptions.ConnectionString"/> and
        ///     <see cref="DatabaseConfigurationOptions.ProviderFactory"/>, or with an
        ///     <see cref="IDbConnectionFactory"/> the application registers, which wins.
        /// </summary>
        public IServiceCollection AddDatabaseConfigurationStore(Action<DatabaseConfigurationOptions> configure)
        {
            services.Configure(configure);

            // TryAdd: an application factory registered before this call wins, and one registered after
            // replaces this as the last registration does.
            services.TryAddSingleton<IDbConnectionFactory, OptionsDbConnectionFactory>();

            // Resolve dialect from options
            services.TryAddSingleton<ISqlDialect>(sp =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseConfigurationOptions>>();
                return SqlDialectFactory.Create(options.Value.Provider);
            });

            services.TryAddSingleton<ConfigurationSchemaManager>();
            // The trail messages, configuration and (soon) persistence all write to. Its dialect is
            // chosen from the same provider option as the configuration one, so the two never disagree
            // about which database they are talking to.
            services.TryAddSingleton<Pragmatic.Audit.AdoNet.IAuditSqlDialect>(sp =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseConfigurationOptions>>();
                return AuditDialectFactory.Create(options.Value.Provider);
            });
            services.TryAddSingleton<Pragmatic.Audit.IAuditDetailRedactor>(new Pragmatic.Audit.PatternAuditDetailRedactor());
            services.TryAddSingleton<Pragmatic.Audit.IAuditSegmentNaming>(new Pragmatic.Audit.HourlyAuditSegmentNaming());
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<Pragmatic.Audit.AuditEntryPreparer>();
            services.TryAddSingleton<Pragmatic.Audit.AdoNet.AdoNetAuditTrail>();
            services.TryAddSingleton<Audit.ConfigurationAuditRecorder>();

            // The store consumes the sensitive-key classifier to mask [Sensitive] values in the audit log.
            // Default treats no key as sensitive; the generated aggregator supersedes it when [Sensitive]
            // properties exist. TryAdd so it resolves even if AddPragmaticConfiguration was not called first.
            services.TryAddSingleton<Pragmatic.Configuration.ISensitiveKeyClassifier>(
                Pragmatic.Configuration.NullSensitiveKeyClassifier.Instance);

            // Replace default in-memory stores with database-backed stores
            services.AddSingleton<IConfigurationStore, DatabaseConfigurationStore>();

            // Re-apply read-through caching so the decorator wraps the database store (idempotent;
            // no-op if AddPragmaticConfiguration disabled caching and never registered the decorator path).
            services.DecorateConfigurationStoreWithCaching();


            return services;
        }

        /// <summary>
        ///     Adds a database-backed secret store with AES-256-GCM encryption.
        ///     The encryption key is resolved via the registered <see cref="IEncryptionKeyProvider"/>.
        ///     By default an <see cref="InlineEncryptionKeyProvider"/> is used, which decodes
        ///     <see cref="DatabaseConfigurationOptions.EncryptionKey"/> (falling back to the
        ///     <c>PRAGMATIC_SECRET_KEY</c> environment variable) — preserving the original behavior.
        ///     Hosts can opt into an alternative source with <see cref="UseEncryptionKeyFromEnvironment"/>
        ///     or <see cref="UseEncryptionKeyFromSecretStore"/>.
        /// </summary>
        public IServiceCollection AddDatabaseSecretStore()
        {
            // Default current-key provider: inline base64 option / PRAGMATIC_SECRET_KEY env var.
            // TryAddSingleton so a host-selected provider (env var / secret store) wins.
            services.TryAddSingleton<IEncryptionKeyProvider, InlineEncryptionKeyProvider>();

            // Default key ring: current key + any PreviousEncryptionKeys (for rotation). A host can replace
            // this to source previous keys elsewhere. TryAdd so a custom ring provider wins.
            services.TryAddSingleton<IEncryptionKeyRingProvider, OptionsEncryptionKeyRingProvider>();

            services.TryAddSingleton<ISecretEncryptor>(sp =>
            {
                var ringProvider = sp.GetRequiredService<IEncryptionKeyRingProvider>();

                // The encryptor is constructed synchronously; resolve the ring once here at build time
                // (rather than per-operation) so the encryptor's own API stays unchanged. Key length is
                // validated per key inside EncryptionKey.FromMaterial.
                var ring = ringProvider.GetKeyRingAsync().AsTask().GetAwaiter().GetResult();

                return new AesGcmSecretEncryptor(ring);
            });

            services.AddSingleton<ISecretStore, DatabaseSecretStore>();

            // Key-rotation re-encrypt pass (run manually after rotating the key).
            services.TryAddSingleton<ISecretKeyRotationService, DatabaseSecretKeyRotationService>();

            // Re-apply read-through caching so the decorator wraps the database secret store
            // (idempotent; no-op when caching was disabled).
            services.DecorateSecretStoreWithCaching();

            return services;
        }

        /// <summary>
        ///     Resolves the secret-store encryption key from the given environment variable (base64-encoded
        ///     32-byte key) instead of the inline configuration option. Call after
        ///     <see cref="AddDatabaseSecretStore"/>.
        /// </summary>
        public IServiceCollection UseEncryptionKeyFromEnvironment(string variableName)
        {
            services.RemoveAll<IEncryptionKeyProvider>();
            services.AddSingleton<IEncryptionKeyProvider>(_ => new EnvironmentEncryptionKeyProvider(variableName));
            return services;
        }

        /// <summary>
        ///     Resolves the secret-store encryption key from the registered <see cref="ISecretStore"/> by the
        ///     given secret name (base64-encoded 32-byte key) — e.g. Azure Key Vault via managed identity.
        ///     Call after <see cref="AddDatabaseSecretStore"/> and after registering the backing secret store.
        /// </summary>
        public IServiceCollection UseEncryptionKeyFromSecretStore(string secretName)
        {
            services.RemoveAll<IEncryptionKeyProvider>();
            services.AddSingleton<IEncryptionKeyProvider>(sp => new SecretStoreEncryptionKeyProvider(sp, secretName));
            return services;
        }
    }
}
