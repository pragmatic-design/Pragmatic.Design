using k8s;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Configuration.Extensions;

namespace Pragmatic.Configuration.Kubernetes;

/// <summary>DI registration for the Kubernetes configuration (ConfigMap) and secret (Secret) backends.</summary>
public static class KubernetesConfigurationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds a ConfigMap-backed configuration store. Replaces the default in-memory configuration store
        ///     and re-applies read-through caching when enabled.
        /// </summary>
        public IServiceCollection AddKubernetesConfigurationStore(Action<KubernetesConfigurationOptions> configure)
        {
            var options = new KubernetesConfigurationOptions();
            configure(options);

            services.TryAddSingleton<IKubernetes>(_ => BuildClient(options));
            services.AddSingleton<IConfigurationStore>(sp =>
                new KubernetesConfigurationStore(
                    new ConfigMapBagApi(sp.GetRequiredService<IKubernetes>(), options.Namespace), options));

            services.DecorateConfigurationStoreWithCaching();
            return services;
        }

        /// <summary>
        ///     Adds a Secret-backed secret store. Replaces the default in-memory secret store and re-applies
        ///     read-through caching when enabled.
        /// </summary>
        public IServiceCollection AddKubernetesSecretStore(Action<KubernetesConfigurationOptions> configure)
        {
            var options = new KubernetesConfigurationOptions();
            configure(options);

            services.TryAddSingleton<IKubernetes>(_ => BuildClient(options));
            services.AddSingleton<ISecretStore>(sp =>
                new KubernetesSecretStore(
                    new SecretBagApi(sp.GetRequiredService<IKubernetes>(), options.Namespace), options));

            services.DecorateSecretStoreWithCaching();
            return services;
        }
    }

    private static k8s.Kubernetes BuildClient(KubernetesConfigurationOptions options)
    {
        var config = options.KubeConfigPath is not null
            ? KubernetesClientConfiguration.BuildConfigFromConfigFile(options.KubeConfigPath)
            : KubernetesClientConfiguration.BuildDefaultConfig();

        return new k8s.Kubernetes(config);
    }
}
