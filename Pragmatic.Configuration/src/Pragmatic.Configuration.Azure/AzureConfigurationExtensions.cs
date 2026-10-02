using Azure.Data.AppConfiguration;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pragmatic.Configuration.Extensions;

namespace Pragmatic.Configuration.Azure;

/// <summary>
///     DI registration for Azure configuration backends.
/// </summary>
public static class AzureConfigurationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds Azure App Configuration as the configuration store backend.
        ///     Uses DefaultAzureCredential for authentication when endpoint is provided,
        ///     or connection string for explicit auth.
        /// </summary>
        /// <remarks>
        ///     The connection string in <see cref="AzureConfigurationOptions.AppConfigurationConnectionString"/>
        ///     is accepted verbatim from the host's configuration/secret pipeline and is not stored or logged by
        ///     this method. In production prefer the credential-based path: set
        ///     <see cref="AzureConfigurationOptions.AppConfigurationEndpoint"/> and authenticate via
        ///     <c>DefaultAzureCredential</c> (managed identity), which avoids embedding a connection string
        ///     entirely. If a connection string must be used, source it from a secret store
        ///     (Key Vault / environment / user-secrets) rather than hard-coding it in configuration files.
        /// </remarks>
        public IServiceCollection AddAzureAppConfigurationStore(Action<AzureConfigurationOptions> configure)
        {
            services.Configure(configure);
            services.AddAzureConfigurationOptionsValidation();

            services.TryAddSingleton(sp =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureConfigurationOptions>>();
                var opts = options.Value;

                if (!string.IsNullOrEmpty(opts.AppConfigurationConnectionString))
                    return new ConfigurationClient(opts.AppConfigurationConnectionString);

                if (!string.IsNullOrEmpty(opts.AppConfigurationEndpoint))
                    return new ConfigurationClient(new Uri(opts.AppConfigurationEndpoint), new DefaultAzureCredential());

                throw new InvalidOperationException(
                    "Azure App Configuration requires either AppConfigurationEndpoint or AppConfigurationConnectionString.");
            });

            services.AddSingleton<IConfigurationStore, AzureAppConfigurationStore>();

            return services;
        }

        /// <summary>
        ///     Adds Azure Key Vault as the secret store backend.
        ///     Uses DefaultAzureCredential for authentication.
        /// </summary>
        public IServiceCollection AddAzureKeyVaultSecretStore(Action<AzureConfigurationOptions>? configure = null)
        {
            if (configure is not null)
                services.Configure(configure);

            services.AddAzureConfigurationOptionsValidation();

            services.TryAddSingleton(sp =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureConfigurationOptions>>();
                var uri = options.Value.KeyVaultUri;

                if (string.IsNullOrEmpty(uri))
                    throw new InvalidOperationException(
                        "Azure Key Vault requires KeyVaultUri to be configured.");

                return new SecretClient(new Uri(uri), new DefaultAzureCredential());
            });

            services.AddSingleton<ISecretStore, AzureKeyVaultSecretStore>();

            // Re-apply read-through caching so the shared decorator wraps the Key Vault store
            // (the Key Vault store does not cache itself). Idempotent; no-op when caching was disabled.
            services.DecorateSecretStoreWithCaching();

            return services;
        }

        /// <summary>
        ///     Adds both Azure App Configuration and Azure Key Vault backends.
        ///     Convenience method for typical Azure deployments.
        /// </summary>
        public IServiceCollection AddAzureConfiguration(Action<AzureConfigurationOptions> configure)
        {
            services.AddAzureAppConfigurationStore(configure);
            services.AddAzureKeyVaultSecretStore();
            return services;
        }

        /// <summary>
        ///     Registers a startup validator enforcing that all cache durations on
        ///     <see cref="AzureConfigurationOptions"/> are strictly positive. Idempotent.
        /// </summary>
        private void AddAzureConfigurationOptionsValidation()
        {
            // Idempotent: AddAzureConfiguration calls both store registrations, each of which
            // would otherwise register a duplicate validator.
            if (services.Any(static d => d.ServiceType == typeof(IValidateOptions<AzureConfigurationOptions>)))
                return;

            services.AddOptions<AzureConfigurationOptions>()
                .Validate(static o => o.Validate() is null, "Invalid AzureConfigurationOptions: cache durations must be greater than zero.")
                .ValidateOnStart();
        }
    }
}
