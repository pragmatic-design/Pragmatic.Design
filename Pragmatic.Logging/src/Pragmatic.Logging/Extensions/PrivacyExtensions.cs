using Pragmatic.Logging.Privacy;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Extension methods for configuring privacy and data redaction features.
/// </summary>
public static class PrivacyExtensions
{
    /// <param name="config">The provider configuration</param>
    extension(PragmaticProviderConfiguration config)
    {
        /// <summary>
        /// Enables data redaction with standard patterns for common sensitive data.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration EnableRedaction()
        {
            config.Privacy.EnableRedaction = true;
            config.Privacy.RedactionMode = RedactionMode.Standard;
            return config;
        }

        /// <summary>
        /// Enables aggressive GDPR-compliant data redaction.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration EnableGdprRedaction()
        {
            config.Privacy.EnableRedaction = true;
            config.Privacy.RedactionMode = RedactionMode.Aggressive;
            config.Privacy.EnableDeepRedaction = true;
            config.Privacy.PreserveLengths = false;
            return config;
        }

        /// <summary>
        /// Configures conservative redaction that only redacts explicitly marked sensitive data.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration EnableConservativeRedaction()
        {
            config.Privacy.EnableRedaction = true;
            config.Privacy.RedactionMode = RedactionMode.Conservative;
            return config;
        }

        /// <summary>
        /// Configures custom redaction with user-defined patterns.
        /// </summary>
        /// <param name="sensitiveProperties">Property names to redact</param>
        /// <param name="patterns">Regex patterns for property names</param>
        /// <param name="messagePatterns">Regex patterns for message content</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration EnableCustomRedaction(string[]? sensitiveProperties = null,
            string[]? patterns = null,
            string[]? messagePatterns = null)
        {
            config.Privacy.EnableRedaction = true;
            config.Privacy.RedactionMode = RedactionMode.Custom;

            if (sensitiveProperties?.Length > 0)
                config.Privacy.SensitivePropertyNames = sensitiveProperties;

            if (patterns?.Length > 0)
                config.Privacy.PropertyNamePatterns = patterns;

            if (messagePatterns?.Length > 0)
                config.Privacy.MessageRedactionPatterns = messagePatterns;

            return config;
        }

        /// <summary>
        /// Disables all data redaction.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration DisableRedaction()
        {
            config.Privacy.EnableRedaction = false;
            config.Privacy.RedactionMode = RedactionMode.None;
            return config;
        }

        /// <summary>
        /// Configures the redaction placeholder text.
        /// </summary>
        /// <param name="placeholder">The placeholder text to use for redacted values</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration WithRedactionPlaceholder(string placeholder)
        {
            config.Privacy.RedactionPlaceholder = placeholder ?? throw new ArgumentNullException(nameof(placeholder));
            return config;
        }

        /// <summary>
        /// Enables length preservation for redacted string values.
        /// </summary>
        /// <param name="preserve">Whether to preserve lengths</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration PreserveLengths(bool preserve = true)
        {
            config.Privacy.PreserveLengths = preserve;
            return config;
        }

        /// <summary>
        /// Enables deep redaction for complex objects.
        /// </summary>
        /// <param name="enable">Whether to enable deep redaction</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration EnableDeepRedaction(bool enable = true)
        {
            config.Privacy.EnableDeepRedaction = enable;
            return config;
        }

        /// <summary>
        /// Adds additional sensitive property names to redact.
        /// </summary>
        /// <param name="propertyNames">Additional property names to redact</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration AddSensitiveProperties(params string[] propertyNames)
        {
            if (propertyNames?.Length > 0)
            {
                config.Privacy.SensitivePropertyNames = config.Privacy.SensitivePropertyNames
                    .Concat(propertyNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            return config;
        }

        /// <summary>
        /// Configures privacy settings to be GDPR compliant using predefined templates.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration UseGdprComplianceTemplate()
        {
            var gdprConfig = ComplianceTemplates.CreateGdprCompliant();
            config.Privacy = gdprConfig;
            return config;
        }

        /// <summary>
        /// Configures privacy settings for HIPAA compliance.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration UseHipaaComplianceTemplate()
        {
            var hipaaConfig = ComplianceTemplates.CreateHipaaCompliant();
            config.Privacy = hipaaConfig;
            return config;
        }

        /// <summary>
        /// Configures privacy settings for PCI-DSS compliance.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration UsePciDssComplianceTemplate()
        {
            var pciConfig = ComplianceTemplates.CreatePciDssCompliant();
            config.Privacy = pciConfig;
            return config;
        }

        /// <summary>
        /// Configures privacy settings for production environment with default redaction rules.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration UseProductionPrivacyDefaults()
        {
            var prodConfig = ComplianceTemplates.CreateProductionDefault();
            config.Privacy = prodConfig;
            return config;
        }

        /// <summary>
        /// Configures privacy settings for development environment with minimal redaction.
        /// </summary>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration UseDevelopmentPrivacyDefaults()
        {
            var devConfig = ComplianceTemplates.CreateDevelopmentFriendly();
            config.Privacy = devConfig;
            return config;
        }

        /// <summary>
        /// Configures multi-compliance privacy settings.
        /// </summary>
        /// <param name="includeGdpr">Include GDPR patterns</param>
        /// <param name="includeHipaa">Include HIPAA patterns</param>
        /// <param name="includePciDss">Include PCI-DSS patterns</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration UseMultiComplianceTemplate(bool includeGdpr = true,
            bool includeHipaa = false,
            bool includePciDss = false)
        {
            var multiConfig = ComplianceTemplates.CreateMultiCompliance(includeGdpr, includeHipaa, includePciDss);
            config.Privacy = multiConfig;
            return config;
        }

        /// <summary>
        /// Enables audit trail for sensitive data access tracking.
        /// </summary>
        /// <param name="auditStorage">The audit storage implementation</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration EnableAuditTrail(IAuditStorage? auditStorage = null)
        {
            config.Privacy.EnableAuditTrail = true;

            if (auditStorage != null)
            {
                AuditTrail.Instance.Configuration.AuditStorage = auditStorage;
            }

            return config;
        }

        /// <summary>
        /// Configures data retention period for compliance.
        /// </summary>
        /// <param name="retentionPeriod">The retention period</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration WithDataRetention(TimeSpan retentionPeriod)
        {
            config.Privacy.DataRetentionPeriod = retentionPeriod;
            return config;
        }

        /// <summary>
        /// Configures explicit consent requirement for data processing.
        /// </summary>
        /// <param name="requireConsent">Whether to require explicit consent</param>
        /// <returns>The configuration for method chaining</returns>
        public PragmaticProviderConfiguration RequireExplicitConsent(bool requireConsent = true)
        {
            config.Privacy.RequireExplicitConsent = requireConsent;
            return config;
        }
    }

    // ================================
    // GDPR Compliance Extensions
    // ================================
}