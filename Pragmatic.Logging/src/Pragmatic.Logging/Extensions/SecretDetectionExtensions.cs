using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Logging.Privacy;
using Pragmatic.Logging.Privacy.Audit;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Extension methods for configuring secret detection services.
/// </summary>
public static class SecretDetectionExtensions
{
    /// <param name="services">The service collection</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds secret detection services to the service collection.
        /// </summary>
        /// <param name="configure">Optional configuration delegate</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddSecretDetection(Action<SecretDetectionOptions>? configure = null)
        {
            var options = SecretDetectionOptions.CreateDefault();
            configure?.Invoke(options);

            services.TryAddSingleton(options);
            services.TryAddSingleton<SecretDetector>();

            return services;
        }

        /// <summary>
        /// Adds secret detection configured for development environments.
        /// </summary>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddSecretDetectionForDevelopment()
        {
            return services.AddSecretDetection(_ => { })
                .Configure<SecretDetectionOptions>(options =>
                {
                    var devOptions = SecretDetectionOptions.CreateForDevelopment();
                    options.MinimumSecretLength = devOptions.MinimumSecretLength;
                    options.MaximumSecretLength = devOptions.MaximumSecretLength;
                    options.PrecompilePatterns = devOptions.PrecompilePatterns;
                    options.EnableMultilineMatching = devOptions.EnableMultilineMatching;
                    options.MaxPatternsPerScan = devOptions.MaxPatternsPerScan;
                    options.RegexTimeoutSeconds = devOptions.RegexTimeoutSeconds;
                    options.SkipPlaceholders = devOptions.SkipPlaceholders;
                    options.SkipFalsePositives = devOptions.SkipFalsePositives;
                    options.MinimumConfidenceForRedaction = devOptions.MinimumConfidenceForRedaction;
                    options.EnableAuditTrail = devOptions.EnableAuditTrail;
                    options.RedactionStyle = devOptions.RedactionStyle;
                });
        }

        /// <summary>
        /// Adds secret detection configured for high-performance scenarios.
        /// </summary>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddSecretDetectionForHighPerformance()
        {
            return services.AddSecretDetection(_ => { })
                .Configure<SecretDetectionOptions>(options =>
                {
                    var perfOptions = SecretDetectionOptions.CreateForHighPerformance();
                    options.MinimumSecretLength = perfOptions.MinimumSecretLength;
                    options.MaximumSecretLength = perfOptions.MaximumSecretLength;
                    options.PrecompilePatterns = perfOptions.PrecompilePatterns;
                    options.EnableMultilineMatching = perfOptions.EnableMultilineMatching;
                    options.MaxPatternsPerScan = perfOptions.MaxPatternsPerScan;
                    options.RegexTimeoutSeconds = perfOptions.RegexTimeoutSeconds;
                    options.SkipPlaceholders = perfOptions.SkipPlaceholders;
                    options.SkipFalsePositives = perfOptions.SkipFalsePositives;
                    options.MinimumConfidenceForRedaction = perfOptions.MinimumConfidenceForRedaction;
                    options.EnableAuditTrail = perfOptions.EnableAuditTrail;
                    options.RedactionStyle = perfOptions.RedactionStyle;
                });
        }

        /// <summary>
        /// Adds secret detection configured for security-focused environments.
        /// </summary>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddSecretDetectionForSecurity()
        {
            return services.AddSecretDetection(_ => { })
                .Configure<SecretDetectionOptions>(options =>
                {
                    var securityOptions = SecretDetectionOptions.CreateForSecurity();
                    options.MinimumSecretLength = securityOptions.MinimumSecretLength;
                    options.MaximumSecretLength = securityOptions.MaximumSecretLength;
                    options.PrecompilePatterns = securityOptions.PrecompilePatterns;
                    options.EnableMultilineMatching = securityOptions.EnableMultilineMatching;
                    options.MaxPatternsPerScan = securityOptions.MaxPatternsPerScan;
                    options.RegexTimeoutSeconds = securityOptions.RegexTimeoutSeconds;
                    options.SkipPlaceholders = securityOptions.SkipPlaceholders;
                    options.SkipFalsePositives = securityOptions.SkipFalsePositives;
                    options.MinimumConfidenceForRedaction = securityOptions.MinimumConfidenceForRedaction;
                    options.EnableAuditTrail = securityOptions.EnableAuditTrail;
                    options.RedactionStyle = securityOptions.RedactionStyle;
                });
        }

        /// <summary>
        /// Configures the data redactor to use secret detection.
        /// </summary>
        /// <param name="enableSecretDetection">Whether to enable secret detection</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection ConfigureDataRedactorWithSecretDetection(bool enableSecretDetection = true)
        {
            return services.Configure<PragmaticDataRedactorConfiguration>(options =>
            {
                options.EnableSecretDetection = enableSecretDetection;
            });
        }

        /// <summary>
        /// Adds a complete privacy protection suite with secret detection.
        /// </summary>
        /// <param name="configureSecretDetection">Optional secret detection configuration</param>
        /// <param name="configureDataRedactor">Optional data redactor configuration</param>
        /// <returns>The service collection for chaining</returns>
        public IServiceCollection AddPrivacyProtectionSuite(Action<SecretDetectionOptions>? configureSecretDetection = null,
            Action<PragmaticDataRedactorConfiguration>? configureDataRedactor = null)
        {
            // Add audit services
            services.AddPragmaticAudit();

            // Add secret detection
            services.AddSecretDetection(configureSecretDetection);

            // Add data redactor with secret detection enabled
            services.TryAddSingleton<PragmaticDataRedactor>(provider =>
            {
                var config = new PragmaticDataRedactorConfiguration();
                configureDataRedactor?.Invoke(config);
                config.EnableSecretDetection = true; // Always enable for privacy suite

                var auditService = provider.GetService<PragmaticAuditService>();
                var secretDetector = provider.GetService<SecretDetector>();

                return new PragmaticDataRedactor(config, auditService, secretDetector);
            });

            return services;
        }
    }
}