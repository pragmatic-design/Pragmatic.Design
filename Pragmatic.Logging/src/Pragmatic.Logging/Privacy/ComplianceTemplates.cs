using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy;

/// <summary>
/// Predefined compliance templates for different regulatory requirements.
/// </summary>
public static class ComplianceTemplates
{
    /// <summary>
    /// Creates a GDPR-compliant privacy configuration.
    /// </summary>
    /// <returns>Privacy configuration optimized for GDPR compliance</returns>
    public static PrivacyConfiguration CreateGdprCompliant()
    {
        // Use GDPR patterns directly

        return new PrivacyConfiguration
        {
            EnableRedaction = true,
            RedactionMode = RedactionMode.Aggressive,
            SensitivePropertyNames = new[]
            {
                "email", "emailAddress", "userEmail", "contactEmail",
                "phone", "phoneNumber", "telephone", "mobile",
                "address", "streetAddress", "homeAddress", "workAddress",
                "postalCode", "zipCode", "postCode",
                "ssn", "socialSecurityNumber", "nationalId",
                "iban", "bankAccount", "accountNumber",
                "creditCard", "cardNumber", "paymentCard",
                "ipAddress", "clientIp", "remoteIp",
                "password", "token", "apiKey", "secret", "authorization",
                "firstName", "lastName", "fullName", "displayName",
                "dateOfBirth", "birthDate", "dob"
            },
            PropertyNamePatterns = CompliancePatterns.Gdpr.PropertyNamePatterns,
            MessageRedactionPatterns = CompliancePatterns.Gdpr.MessageContentPatterns,
            RedactionPlaceholder = "[GDPR-REDACTED]",
            PreserveLengths = false, // For GDPR, complete redaction is preferred
            PreserveJsonStructure = true,
            EnableDeepRedaction = true,
            EnableAuditTrail = true,
            ComplianceStandard = ComplianceStandard.Gdpr,
            RequireExplicitConsent = true,
            DataRetentionPeriod = TimeSpan.FromDays(730) // 2 years default
        };
    }

    /// <summary>
    /// Creates a HIPAA-compliant privacy configuration for healthcare.
    /// </summary>
    /// <returns>Privacy configuration optimized for HIPAA compliance</returns>
    public static PrivacyConfiguration CreateHipaaCompliant()
    {
        // Use HIPAA patterns directly

        return new PrivacyConfiguration
        {
            EnableRedaction = true,
            RedactionMode = RedactionMode.Aggressive,
            SensitivePropertyNames = new[]
            {
                "ssn", "socialSecurityNumber",
                "medicalRecordNumber", "mrn", "patientId",
                "healthPlanNumber", "memberId",
                "dateOfBirth", "dob", "birthDate",
                "diagnosis", "treatment", "medication",
                "patientName", "firstName", "lastName",
                "address", "phoneNumber", "email"
            },
            PropertyNamePatterns = CompliancePatterns.Hipaa.PropertyNamePatterns,
            MessageRedactionPatterns = CompliancePatterns.Hipaa.MessageContentPatterns,
            RedactionPlaceholder = "[PHI-REDACTED]",
            PreserveLengths = false,
            PreserveJsonStructure = true,
            EnableDeepRedaction = true,
            EnableAuditTrail = true,
            ComplianceStandard = ComplianceStandard.Hipaa,
            RequireExplicitConsent = true,
            DataRetentionPeriod = TimeSpan.FromDays(2190) // 6 years for HIPAA
        };
    }

    /// <summary>
    /// Creates a PCI-DSS compliant privacy configuration for payment data.
    /// </summary>
    /// <returns>Privacy configuration optimized for PCI-DSS compliance</returns>
    public static PrivacyConfiguration CreatePciDssCompliant()
    {
        // Use PCI-DSS patterns directly

        return new PrivacyConfiguration
        {
            EnableRedaction = true,
            RedactionMode = RedactionMode.Aggressive,
            SensitivePropertyNames = new[]
            {
                "creditCard", "cardNumber", "pan", "primaryAccountNumber",
                "cvv", "cvc", "securityCode",
                "expirationDate", "expiry", "validThru",
                "trackData", "magneticStripe",
                "cardholderName", "paymentMethod"
            },
            PropertyNamePatterns = CompliancePatterns.PciDss.PropertyNamePatterns,
            MessageRedactionPatterns = CompliancePatterns.PciDss.MessageContentPatterns,
            RedactionPlaceholder = "[CARD-DATA-REDACTED]",
            PreserveLengths = false, // Complete redaction for payment data
            PreserveJsonStructure = true,
            EnableDeepRedaction = true,
            EnableAuditTrail = true,
            ComplianceStandard = ComplianceStandard.PciDss,
            RequireExplicitConsent = false, // Payment processing consent is implicit
            DataRetentionPeriod = TimeSpan.FromDays(365) // 1 year for PCI-DSS
        };
    }

    /// <summary>
    /// Creates a production-ready privacy configuration with default redaction rules.
    /// </summary>
    /// <returns>Privacy configuration suitable for production environments</returns>
    public static PrivacyConfiguration CreateProductionDefault()
    {
        return new PrivacyConfiguration
        {
            EnableRedaction = true,
            RedactionMode = RedactionMode.Standard,
            SensitivePropertyNames = new[]
            {
                "password", "pwd", "secret", "token", "apiKey", "key",
                "authorization", "auth", "bearer",
                "email", "emailAddress",
                "phone", "phoneNumber",
                "address", "ipAddress",
                "ssn", "creditCard", "cardNumber"
            },
            PropertyNamePatterns = new[]
            {
                @".*[Pp]assword.*",
                @".*[Ss]ecret.*",
                @".*[Tt]oken.*",
                @".*[Ee]mail.*",
                @".*[Pp]hone.*",
                @".*[Aa]ddress.*",
                // Short terms as words (PropertyNameWord): as substrings "ip" masked "Shipping",
                // "Description" and "Recipient", "key" masked "Monkey", "auth" masked "Author".
                @"(?i).*(?:api|private|access|signing)[_-]?key.*",
                PropertyNameWord.Pattern("key"),
                PropertyNameWord.Pattern("auth"),
                PropertyNameWord.Pattern("ip")
            },
            MessageRedactionPatterns = new[]
            {
                CompliancePatterns.Gdpr.Email,
                CompliancePatterns.Gdpr.PhoneNumber,
                CompliancePatterns.Gdpr.IpAddress,
                CompliancePatterns.Gdpr.CreditCard
            },
            RedactionPlaceholder = "[REDACTED]",
            PreserveLengths = true,
            PreserveJsonStructure = true,
            EnableDeepRedaction = false, // Conservative for performance
            EnableAuditTrail = false, // Disable by default for performance
            ComplianceStandard = ComplianceStandard.General,
            RequireExplicitConsent = false,
            DataRetentionPeriod = TimeSpan.FromDays(90) // 3 months default
        };
    }

    /// <summary>
    /// Creates a combined compliance configuration for multiple standards.
    /// </summary>
    /// <param name="includeGdpr">Include GDPR patterns</param>
    /// <param name="includeHipaa">Include HIPAA patterns</param>
    /// <param name="includePciDss">Include PCI-DSS patterns</param>
    /// <returns>Combined compliance configuration</returns>
    public static PrivacyConfiguration CreateMultiCompliance(
        bool includeGdpr = true,
        bool includeHipaa = false,
        bool includePciDss = false)
    {
        var baseConfig = CreateGdprCompliant(); // Start with GDPR as base
        var patterns = CompliancePatterns.CreateCombined(includeGdpr, includeHipaa, includePciDss);

        baseConfig.PropertyNamePatterns = patterns.PropertyNamePatterns.ToArray();
        baseConfig.MessageRedactionPatterns = patterns.MessageContentPatterns.ToArray();
        baseConfig.RedactionPlaceholder = "[MULTI-COMPLIANCE-REDACTED]";

        // Use the most restrictive settings for multi-compliance
        if (includeHipaa)
        {
            baseConfig.DataRetentionPeriod = TimeSpan.FromDays(2190); // HIPAA requirement
        }

        var standards = new List<ComplianceStandard>();
        if (includeGdpr)
            standards.Add(ComplianceStandard.Gdpr);
        if (includeHipaa)
            standards.Add(ComplianceStandard.Hipaa);
        if (includePciDss)
            standards.Add(ComplianceStandard.PciDss);

        // ComplianceStandard is not a [Flags] enum, so a single field cannot represent
        // multiple standards. Keep the most restrictive as primary and surface the rest via
        // AdditionalComplianceStandards so ALL selected standards are applied/reported — not
        // just the first one. (The redaction patterns themselves are already combined above
        // via CreateCombined, so masking behavior covers every selected standard.)
        if (standards.Count > 0)
        {
            // Restrictiveness order: HIPAA > PCI-DSS > GDPR (longest retention / strictest PHI).
            var primary = standards.Contains(ComplianceStandard.Hipaa) ? ComplianceStandard.Hipaa
                        : standards.Contains(ComplianceStandard.PciDss) ? ComplianceStandard.PciDss
                        : standards[0];

            baseConfig.ComplianceStandard = primary;
            baseConfig.AdditionalComplianceStandards = standards.Where(s => s != primary).ToArray();
        }

        return baseConfig;
    }

    /// <summary>
    /// Creates a development-friendly privacy configuration with minimal redaction.
    /// </summary>
    /// <returns>Privacy configuration suitable for development environments</returns>
    public static PrivacyConfiguration CreateDevelopmentFriendly()
    {
        return new PrivacyConfiguration
        {
            EnableRedaction = true,
            RedactionMode = RedactionMode.Conservative,
            SensitivePropertyNames = new[]
            {
                "password", "secret", "token", "apiKey"
            },
            PropertyNamePatterns = new[]
            {
                @".*[Pp]assword.*",
                @".*[Ss]ecret.*",
                @".*[Tt]oken.*",
                @"(?i).*(?:api|private|access|signing)[_-]?key.*",
                PropertyNameWord.Pattern("key")
            },
            MessageRedactionPatterns = Array.Empty<string>(), // No message redaction in dev
            RedactionPlaceholder = "[DEV-REDACTED]",
            PreserveLengths = true,
            PreserveJsonStructure = true,
            EnableDeepRedaction = false,
            EnableAuditTrail = false,
            ComplianceStandard = ComplianceStandard.Development,
            RequireExplicitConsent = false,
            DataRetentionPeriod = TimeSpan.FromDays(7) // Short retention in dev
        };
    }
}

