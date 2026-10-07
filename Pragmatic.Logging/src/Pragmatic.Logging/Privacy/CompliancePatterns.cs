namespace Pragmatic.Logging.Privacy;

/// <summary>
/// Predefined patterns for common compliance requirements (GDPR, HIPAA, PCI-DSS).
/// </summary>
public static class CompliancePatterns
{
    /// <summary>
    /// GDPR compliance patterns for European data protection.
    /// </summary>
    public static class Gdpr
    {
        /// <summary>
        /// Email address pattern for GDPR compliance.
        /// </summary>
        /// <remarks>
        ///     From <c>Pragmatic.Redaction</c>, so the log and the audit trail agree on what an
        ///     e-mail looks like. The literal it replaced had <c>[A-Z|a-z]</c> — a character class
        ///     holding a literal pipe, a typo for an alternation.
        /// </remarks>
        public static readonly string Email = global::Pragmatic.Redaction.PersonalDataPatterns.Text.Email;

        /// <summary>
        /// European phone number patterns.
        /// </summary>
        public static readonly string PhoneNumber = @"(\+\d{1,3}\s?)?\(?\d{1,4}\)?[\s.-]?\d{1,4}[\s.-]?\d{1,9}";

        /// <summary>
        /// IBAN (International Bank Account Number) pattern.
        /// </summary>
        /// <remarks>From <c>Pragmatic.Redaction</c>; behaviour verified identical on a shared corpus.</remarks>
        public static readonly string Iban = global::Pragmatic.Redaction.PersonalDataPatterns.Text.Iban;

        /// <summary>
        /// European Social Security Numbers (various formats).
        /// </summary>
        public static readonly string SocialSecurityNumber = @"\b\d{3}-\d{2}-\d{4}\b|\b\d{9}\b";

        /// <summary>
        /// IP Address pattern for privacy compliance.
        /// </summary>
        public static readonly string IpAddress = @"\b(?:\d{1,3}\.){3}\d{1,3}\b";

        /// <summary>
        /// Credit card number pattern.
        /// </summary>
        public static readonly string CreditCard = @"\b(?:\d{4}[\s-]?){3}\d{4}\b";

        /// <summary>
        /// Postal code patterns for various European countries.
        /// </summary>
        public static readonly string PostalCode = @"\b\d{5}(-\d{4})?\b|\b[A-Z]\d[A-Z]\s?\d[A-Z]\d\b|\b\d{4}\s?[A-Z]{2}\b";

        /// <summary>
        /// Gets all GDPR compliance property name patterns.
        /// </summary>
        public static string[] PropertyNamePatterns => new[]
        {
            @".*[Ee]mail.*",
            @".*[Pp]hone.*",
            @".*[Tt]elephone.*",
            @".*[Aa]ddress.*",
            @".*[Pp]ostal.*",
            // Short terms as words (PropertyNameWord): as substrings they masked "Unzip" and "ProcessName".
            PropertyNameWord.Pattern("zip"),
            PropertyNameWord.Pattern("ssn"),
            @".*[Ss]ocial.*",
            @".*[Ii]ban.*",
            @".*[Cc]redit[Cc]ard.*",
            @".*[Cc]ard[Nn]umber.*",
            @".*[Ii]p[Aa]ddress.*",
            @".*[Pp]assword.*",
            @".*[Tt]oken.*",
            @".*[Aa]pi[Kk]ey.*",
            @".*[Ss]ecret.*",
            @".*[Aa]uthorization.*"
        };

        /// <summary>
        /// Gets all GDPR compliance message content patterns.
        /// </summary>
        public static string[] MessageContentPatterns => new[]
        {
            Email,
            PhoneNumber,
            Iban,
            SocialSecurityNumber,
            IpAddress,
            CreditCard,
            PostalCode
        };
    }

    /// <summary>
    /// HIPAA compliance patterns for healthcare data.
    /// </summary>
    public static class Hipaa
    {
        /// <summary>
        /// US Social Security Number pattern.
        /// </summary>
        public static readonly string Ssn = @"\b\d{3}-\d{2}-\d{4}\b";

        /// <summary>
        /// Medical Record Number pattern.
        /// </summary>
        public static readonly string MedicalRecordNumber = @"\bMRN[\s:]?\d{6,10}\b";

        /// <summary>
        /// Health Plan Beneficiary Number pattern.
        /// </summary>
        public static readonly string HealthPlanNumber = @"\b\d{3}-\d{2}-\d{4}[A-Z]?\b";

        /// <summary>
        /// Date of Birth pattern.
        /// </summary>
        public static readonly string DateOfBirth = @"\b\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4}\b";

        /// <summary>
        /// Gets all HIPAA compliance property name patterns.
        /// </summary>
        public static string[] PropertyNamePatterns => new[]
        {
            // Short terms as words (PropertyNameWord): as substrings they masked "ProcessName" and "Adobe".
            PropertyNameWord.Pattern("ssn"),
            @".*[Ss]ocial.*",
            @".*[Mm]edical.*[Rr]ecord.*",
            PropertyNameWord.Pattern("mrn"),
            @".*[Hh]ealth.*[Pp]lan.*",
            @".*[Dd]ate.*[Bb]irth.*",
            PropertyNameWord.Pattern("dob"),
            @".*[Pp]atient.*[Ii]d.*",
            @".*[Dd]iagnosis.*",
            @".*[Tt]reatment.*"
        };

        /// <summary>
        /// Gets all HIPAA compliance message content patterns.
        /// </summary>
        public static string[] MessageContentPatterns => new[]
        {
            Ssn,
            MedicalRecordNumber,
            HealthPlanNumber,
            DateOfBirth
        };
    }

    /// <summary>
    /// PCI-DSS compliance patterns for payment data.
    /// </summary>
    public static class PciDss
    {
        /// <summary>
        /// Credit card number pattern (various formats).
        /// </summary>
        /// <remarks>
        ///     The same regex as <see cref="Gdpr.CreditCard" />, under a second name. Aliased rather
        ///     than repeated: two copies of one pattern drift, and only one of them gets fixed.
        /// </remarks>
        public static readonly string CreditCardNumber = Gdpr.CreditCard;

        /// <summary>
        /// CVV/CVC pattern.
        /// </summary>
        /// <remarks>
        /// Requires a CVV/CVC/security-code label adjacent to the digits. A bare 3-4 digit
        /// run is far too common (years, counts, ids) to mask unconditionally — matching it
        /// produced massive false positives. The label requirement keeps detection scoped to
        /// an actual card-security context. The 3-4 digit value is captured in group 1.
        /// </remarks>
        public static readonly string Cvv = @"(?i)\b(?:cvv|cvc|cvv2|cvc2|cid|security\s*code|card\s*verification(?:\s*(?:value|code))?)\b\s*[:=]?\s*(\d{3,4})\b";

        /// <summary>
        /// Expiration date pattern.
        /// </summary>
        public static readonly string ExpirationDate = @"\b\d{1,2}[/\-]\d{2,4}\b";

        /// <summary>
        /// Track data pattern.
        /// </summary>
        public static readonly string TrackData = @"%[A-Z0-9]+\?";

        /// <summary>
        /// Gets all PCI-DSS compliance property name patterns.
        /// </summary>
        public static string[] PropertyNamePatterns => new[]
        {
            @".*[Cc]redit[Cc]ard.*",
            @".*[Cc]ard[Nn]umber.*",
            // Short terms as words (PropertyNameWord): as substrings they masked "Company", "Span" and
            // "TrackingNumber".
            PropertyNameWord.Pattern("pan"),
            @".*[Cc]vv.*",
            @".*[Cc]vc.*",
            @".*[Ee]xpir.*",
            PropertyNameWord.Pattern("track"),
            @".*[Pp]ayment.*"
        };

        /// <summary>
        /// Gets all PCI-DSS compliance message content patterns.
        /// </summary>
        public static string[] MessageContentPatterns => new[]
        {
            CreditCardNumber,
            Cvv,
            ExpirationDate,
            TrackData
        };
    }

    /// <summary>
    /// Creates a combined pattern configuration for multiple compliance standards.
    /// </summary>
    /// <param name="includeGdpr">Include GDPR patterns</param>
    /// <param name="includeHipaa">Include HIPAA patterns</param>
    /// <param name="includePciDss">Include PCI-DSS patterns</param>
    /// <returns>Combined patterns configuration</returns>
    public static CompliancePatternsConfig CreateCombined(
        bool includeGdpr = true,
        bool includeHipaa = false,
        bool includePciDss = false)
    {
        var config = new CompliancePatternsConfig();

        if (includeGdpr)
        {
            config.PropertyNamePatterns.AddRange(Gdpr.PropertyNamePatterns);
            config.MessageContentPatterns.AddRange(Gdpr.MessageContentPatterns);
            config.ComplianceStandards.Add("GDPR");
        }

        if (includeHipaa)
        {
            config.PropertyNamePatterns.AddRange(Hipaa.PropertyNamePatterns);
            config.MessageContentPatterns.AddRange(Hipaa.MessageContentPatterns);
            config.ComplianceStandards.Add("HIPAA");
        }

        if (includePciDss)
        {
            config.PropertyNamePatterns.AddRange(PciDss.PropertyNamePatterns);
            config.MessageContentPatterns.AddRange(PciDss.MessageContentPatterns);
            config.ComplianceStandards.Add("PCI-DSS");
        }

        return config;
    }
}

/// <summary>
/// Configuration for compliance patterns.
/// </summary>
public sealed class CompliancePatternsConfig
{
    /// <summary>
    /// Gets the list of property name patterns.
    /// </summary>
    public List<string> PropertyNamePatterns { get; } = new();

    /// <summary>
    /// Gets the list of message content patterns.
    /// </summary>
    public List<string> MessageContentPatterns { get; } = new();

    /// <summary>
    /// Gets the list of compliance standards included.
    /// </summary>
    public List<string> ComplianceStandards { get; } = new();
}