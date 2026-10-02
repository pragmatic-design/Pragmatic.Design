using Google.Cloud.SecretManager.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Configuration.Extensions;

namespace Pragmatic.Configuration.Gcp;

/// <summary>DI registration for the GCP Secret Manager backend.</summary>
public static class GcpConfigurationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds a GCP Secret Manager-backed secret store. Uses Application Default Credentials. Replaces the
        ///     default in-memory secret store and re-applies read-through caching when enabled.
        /// </summary>
        public IServiceCollection AddGcpSecretStore(Action<GcpConfigurationOptions> configure)
        {
            var options = new GcpConfigurationOptions();
            configure(options);
            if (string.IsNullOrEmpty(options.ProjectId))
                throw new InvalidOperationException("GcpConfigurationOptions.ProjectId must be set.");

            services.AddSingleton(options);
            services.TryAddSingleton(_ => SecretManagerServiceClient.Create());
            services.AddSingleton<ISecretStore>(sp =>
                new GcpSecretStore(sp.GetRequiredService<SecretManagerServiceClient>(), options));

            services.DecorateSecretStoreWithCaching();
            return services;
        }
    }
}
