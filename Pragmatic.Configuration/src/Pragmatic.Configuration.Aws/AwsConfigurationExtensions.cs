using Amazon;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SimpleSystemsManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Configuration.Extensions;

namespace Pragmatic.Configuration.Aws;

/// <summary>DI registration for the AWS configuration backends (Secrets Manager + SSM Parameter Store).</summary>
public static class AwsConfigurationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds an AWS Secrets Manager-backed secret store. Replaces the default in-memory secret store and
        ///     re-applies read-through caching when enabled.
        /// </summary>
        public IServiceCollection AddAwsSecretStore(Action<AwsConfigurationOptions> configure)
        {
            var options = Configure(configure);

            services.TryAddSingleton<IAmazonSecretsManager>(_ =>
            {
                var config = Config(new AmazonSecretsManagerConfig(), options);
                return HasExplicitCredentials(options, out var creds)
                    ? new AmazonSecretsManagerClient(creds, config)
                    : new AmazonSecretsManagerClient(config);
            });

            services.AddSingleton<ISecretStore>(sp =>
                new AwsSecretStore(sp.GetRequiredService<IAmazonSecretsManager>(), options));

            services.DecorateSecretStoreWithCaching();
            return services;
        }

        /// <summary>
        ///     Adds an AWS SSM Parameter Store-backed configuration store. Replaces the default in-memory
        ///     configuration store and re-applies read-through caching when enabled.
        /// </summary>
        public IServiceCollection AddAwsParameterStore(Action<AwsConfigurationOptions> configure)
        {
            var options = Configure(configure);

            services.TryAddSingleton<IAmazonSimpleSystemsManagement>(_ =>
            {
                var config = Config(new AmazonSimpleSystemsManagementConfig(), options);
                return HasExplicitCredentials(options, out var creds)
                    ? new AmazonSimpleSystemsManagementClient(creds, config)
                    : new AmazonSimpleSystemsManagementClient(config);
            });

            services.AddSingleton<IConfigurationStore>(sp =>
                new AwsParameterStore(sp.GetRequiredService<IAmazonSimpleSystemsManagement>(), options));

            services.DecorateConfigurationStoreWithCaching();
            return services;
        }
    }

    private static AwsConfigurationOptions Configure(Action<AwsConfigurationOptions> configure)
    {
        var options = new AwsConfigurationOptions();
        configure(options);
        return options;
    }

    /// <summary>
    ///     Returns explicit static credentials when both keys are set; otherwise <c>false</c> so the client is
    ///     built without them and resolves the SDK's default credential chain (env, profile, IAM role, …).
    /// </summary>
    private static bool HasExplicitCredentials(AwsConfigurationOptions options, out AWSCredentials credentials)
    {
        if (options is { AccessKey: not null, SecretKey: not null })
        {
            credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
            return true;
        }

        credentials = null!;
        return false;
    }

    private static TConfig Config<TConfig>(TConfig config, AwsConfigurationOptions options)
        where TConfig : ClientConfig
    {
        if (!string.IsNullOrEmpty(options.ServiceUrl))
        {
            // Custom endpoint (e.g. LocalStack): target it directly and pin an auth region for signing.
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region ?? "us-east-1";
        }
        else if (!string.IsNullOrEmpty(options.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        return config;
    }
}
