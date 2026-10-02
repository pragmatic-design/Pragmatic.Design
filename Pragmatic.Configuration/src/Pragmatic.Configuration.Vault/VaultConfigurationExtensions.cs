using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Configuration.Extensions;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;

namespace Pragmatic.Configuration.Vault;

/// <summary>DI registration for the HashiCorp Vault secret backend.</summary>
public static class VaultConfigurationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds a HashiCorp Vault-backed secret store (KV v2). Replaces the default in-memory secret store
        ///     and re-applies read-through caching when enabled.
        /// </summary>
        public IServiceCollection AddVaultSecretStore(Action<VaultConfigurationOptions> configure)
        {
            var options = new VaultConfigurationOptions();
            configure(options);
            services.AddSingleton(options);

            services.TryAddSingleton<IVaultClient>(_ =>
            {
                IAuthMethodInfo auth = new TokenAuthMethodInfo(options.Token);
                return new VaultClient(new VaultClientSettings(options.Address, auth));
            });

            services.AddSingleton<ISecretStore, VaultSecretStore>();

            // Re-apply read-through caching so the decorator wraps the Vault store (idempotent; no-op when
            // caching was disabled).
            services.DecorateSecretStoreWithCaching();

            return services;
        }
    }
}
