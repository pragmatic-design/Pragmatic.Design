# Privacy and Security

Production log streams inevitably capture sensitive data -- passwords embedded in connection strings, credit card numbers passed as query parameters, or personal identifiers logged during business operations. Pragmatic.Logging provides a layered defense that detects, redacts, and audits sensitive data before it reaches any output provider, so your logs stay compliant without requiring manual review of every log statement.

---

## 1. Secret Detection

Accidental exposure of API keys, JWT tokens, or private keys in logs can lead to credential compromise. The `SecretDetector` scans log messages and property values against a curated library of pre-compiled regex patterns, assigning a confidence score and severity level to each detection.

### Pattern Library

The `SecretPatterns` class organizes detection patterns into six categories.

| Category | Examples | Severity |
|----------|----------|----------|
| `ApiKeys` | AWS Access Key (`AKIA...`), GitHub PAT (`ghp_...`), Stripe Key (`sk_live_...`), Google API Key (`AIza...`) | Critical |
| `Tokens` | JWT (`eyJ...`), Bearer tokens, OAuth access/refresh tokens, session tokens | High |
| `Crypto` | RSA/EC/PGP private keys, PKCS#8 keys, X.509 certificates, Base64 keys | Critical |
| `Database` | SQL Server `Password=`, MongoDB URI with credentials, Redis password, generic DB URLs | Critical |
| `Cloud` | Azure Storage Account Key, GCP Service Account Key, DigitalOcean PAT | Critical |
| `Application` | Encryption keys, hash salts, ASP.NET Machine Keys, GUIDs | Medium |

### Confidence Scoring

Each detection carries a confidence score between 0.0 and 1.0. The `SecretDetectionOptions.MinimumConfidenceForRedaction` threshold (default: 0.7) controls which detections trigger automatic redaction. Lower the threshold in development to catch test secrets; raise it in high-performance scenarios to reduce false positives.

```csharp
var options = SecretDetectionOptions.CreateDefault();
options.MinimumConfidenceForRedaction = 0.7;  // Production default
options.PrecompilePatterns = true;            // Compile regex at startup
options.RegexTimeoutSeconds = 2.0;           // Safety timeout per pattern
options.MaxPatternsPerScan = 50;             // Limit patterns for performance
```

### SecretDetectionOptions Presets

| Preset | Confidence | Patterns | Use case |
|--------|------------|----------|----------|
| `CreateDefault()` | 0.7 | 50 max | Production balanced |
| `CreateForDevelopment()` | 0.5 | Unlimited | Catch test secrets |
| `CreateForHighPerformance()` | 0.9 | 20 max | Minimal overhead |
| `CreateForSecurity()` | 0.3 | Unlimited | Maximum detection |

### Redaction Styles

When a secret is detected, the `SecretRedactionStyle` controls how it is replaced.

| Style | Output | Description |
|-------|--------|-------------|
| `Placeholder` | `[API_KEY_REDACTED]` | Descriptive placeholder with secret type |
| `PreserveLength` | `********************************` | Asterisks matching original length |
| `PreserveStructure` | `eyJ***.***.***` | Keeps format markers (JWT segments) |
| `Minimal` | `[REDACTED]` | Simple constant replacement |

### Critical Patterns

For scenarios where you want only the highest-priority detections, `SecretPatterns.GetCriticalPatterns()` returns the subset that should never appear in logs: private keys, cloud credentials, database passwords, and JWT tokens.

---

## 2. Data Redaction

Beyond secret detection, business applications need to redact personally identifiable information (PII), financial data, and health records based on property names and message content patterns. The `IDataRedactor` interface and its implementation `PragmaticDataRedactor` provide this capability.

### How Redaction Works

The redactor evaluates each log property in three steps:

1. **Explicit flag** -- Properties marked with `PropertyCharacteristics.Redacted` are always redacted.
2. **Name matching** -- The property name is checked against a `HashSet<string>` of known sensitive names (case-insensitive) and an array of compiled regex patterns.
3. **Message scanning** -- The log message text is scanned against message redaction patterns (emails, credit cards, SSNs, phone numbers).

### IDataRedactor Interface

```csharp
public interface IDataRedactor
{
    bool ShouldRedact(string propertyName, PropertyCharacteristics characteristics);
    Dictionary<string, object?> RedactProperties(Dictionary<string, object?> properties);
    Dictionary<string, object?> RedactProperties(
        Dictionary<string, object?> properties,
        LogLevel logLevel, string categoryName,
        string? userId = null, string? correlationId = null);
    string RedactMessage(string message);
}
```

### Configuration

```csharp
var redactorConfig = PragmaticDataRedactorConfiguration.CreateDefault();

// Add application-specific sensitive properties
redactorConfig.SensitivePropertyNames = new[]
{
    "password", "token", "apiKey", "secret",
    "creditCard", "ssn", "email", "phoneNumber"
};

// Regex patterns for property names
redactorConfig.PropertyNamePatterns = new[]
{
    @"(?i).*password.*",
    @"(?i).*secret.*",
    @"(?i).*token.*"
};

// Patterns for message content redaction
redactorConfig.MessageRedactionPatterns = new[]
{
    @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",  // Email
    @"\b(?:\d{4}[-\s]?){3}\d{4}\b",                             // Credit card
    @"\b\d{3}-?\d{2}-?\d{4}\b"                                   // SSN
};

// Redaction behavior
redactorConfig.RedactionPlaceholder = "[REDACTED]";
redactorConfig.PreserveLengths = false;
redactorConfig.PreserveJsonStructure = true;
redactorConfig.EnableDeepRedaction = false;

var redactor = new PragmaticDataRedactor(redactorConfig);
```

**How the shipped name patterns match.** The defaults (`CreateDefault`, `CreateGdprCompliant`, the
compliance templates and `CompliancePatterns`) match a short term as a *word* of the property name,
not as letters inside another word: `ssn` masks `Ssn`, `CustomerSsn` and `CUSTOMER_SSN`, but not
`ProcessName`; `pin` masks `UserPin` but not `Shipping`; `ip` masks `ClientIP` but not `Description`.
Words are split at case changes, `_` and `-`. Long, unambiguous terms (`password`, `secret`, `token`,
`email`) are still matched anywhere in the name, so an all-lowercase name such as `accesstoken` is
caught; compounds like `apikey` are listed explicitly. Patterns you write yourself are used as written,
compiled case-insensitively: `.*pin.*` still masks `Shipping`.

### Redaction Modes

The `RedactionMode` enum on `PrivacyConfiguration` controls the aggressiveness of automatic redaction.

| Mode | Behavior |
|------|----------|
| `None` | No automatic redaction |
| `Conservative` | Only explicitly marked sensitive data |
| `Standard` | Common sensitive patterns (passwords, tokens, emails) |
| `Aggressive` | GDPR-compliant with extensive PII patterns |
| `Custom` | User-defined patterns only |

### JSON Structure Preservation

When `PreserveJsonStructure` is enabled, the redactor replaces values while keeping the JSON shape intact. Strings become `[REDACTED]`, numbers become `0`, booleans become `false`, and objects become `{"redacted": true}`. This allows downstream parsers to process the log entry without schema errors.

---

## 3. Compliance Standards

Different industries require different redaction rules. Pragmatic.Logging provides pre-built compliance templates that configure the correct sensitive property lists, message patterns, redaction placeholders, audit requirements, and data retention periods for each standard.

### Using Compliance Presets

```csharp
// Apply a single compliance standard
var gdprConfig = ComplianceTemplates.CreateGdprCompliant();
var hipaaConfig = ComplianceTemplates.CreateHipaaCompliant();
var pciConfig = ComplianceTemplates.CreatePciDssCompliant();

// Multi-compliance (uses the most restrictive settings)
var multiConfig = ComplianceTemplates.CreateMultiCompliance(
    includeGdpr: true,
    includeHipaa: true,
    includePciDss: false);
```

### Compliance Template Summary

| Standard | Placeholder | Deep Redaction | Audit Trail | Retention | Consent Required |
|----------|-------------|---------------|-------------|-----------|-----------------|
| **GDPR** | `[GDPR-REDACTED]` | Yes | Yes | 2 years | Yes |
| **HIPAA** | `[PHI-REDACTED]` | Yes | Yes | 6 years | Yes |
| **PCI-DSS** | `[CARD-DATA-REDACTED]` | Yes | Yes | 1 year | No |
| **CCPA** | `[REDACTED]` | Yes | Yes | 90 days | Yes |
| **SOX** | `[REDACTED]` | Yes | Yes | 7 years | No |
| **Production Default** | `[REDACTED]` | No | No | 90 days | No |
| **Development** | `[DEV-REDACTED]` | No | No | 7 days | No |

### What Each Template Protects

**GDPR** -- Names, addresses, email, phone, date of birth, IP addresses, location data, nationality, passport/license numbers, user IDs, customer/client references.

**HIPAA** -- SSN, medical record numbers, patient IDs, health plan numbers, diagnosis, treatment, medication, dates of birth, and all GDPR personal data.

**PCI-DSS** -- Credit card numbers (PAN), CVV/CVC, expiration dates, magnetic stripe data, cardholder names, payment methods.

---

## 4. Audit Trail

Compliance frameworks require proof that sensitive data was handled correctly. The `PragmaticAuditService` records every redaction event, sensitive data access, and compliance violation into a durable audit store with batch processing and configurable flush intervals.

> This is a record of what the logging pipeline did, not the application's audit trail: it is not
> tamper-evident and nothing verifies it. Who did what, on whose authority, belongs on
> [Pragmatic.Audit](../../Pragmatic.Audit/README.md).

### PragmaticAuditService

The audit service implements `IHostedService` and runs as a background service. It queues audit entries in a `ConcurrentQueue` and flushes them to storage in batches.

```csharp
// Recording a redaction event (called automatically by PragmaticDataRedactor)
auditService.RecordRedaction(
    LogLevel.Information,
    categoryName: "OrderService",
    propertyName: "CreditCardNumber",
    originalLength: 16,
    redactionReason: RedactionReason.ComplianceRequirement,
    complianceStandard: ComplianceStandard.PciDss,
    userId: "admin-42",
    correlationId: "corr-abc-123");

// Recording sensitive data access
auditService.RecordSensitiveDataAccess(
    userId: "support-agent-7",
    dataType: "CustomerPII",
    accessReason: "Support ticket #12345",
    correlationId: "corr-def-456",
    complianceStandard: ComplianceStandard.Gdpr);

// Recording a compliance violation (forces immediate flush)
auditService.RecordComplianceViolation(
    violationType: "UnencryptedPII",
    description: "Customer email logged without redaction in PaymentService",
    complianceStandard: ComplianceStandard.Gdpr,
    severity: AuditSeverity.High);
```

### Storage Options

| Storage | Class | Persistence | Best For |
|---------|-------|-------------|----------|
| Memory | `MemoryAuditStorage` | In-process | Testing, development |
| File System | `FileSystemAuditStorage` | Disk | Single-server deployments |

### Audit Policies

Audit policies control which events are recorded and how entries are transformed before storage. The `IAuditPolicy` interface defines `ShouldAudit(context)`, `ShouldFlushImmediately(entry)`, and `TransformEntry(entry)`.

| Policy | Description |
|--------|-------------|
| Default | Records all redaction and access events |
| GDPR | Records everything, forces immediate flush for violations |
| HighPerformance | Only records high-severity events |
| Development | Minimal auditing for fast iteration |

### Querying Audit Data

```csharp
// Time-range query
var entries = await auditService.GetAuditEntriesAsync(
    from: DateTime.UtcNow.AddDays(-7),
    until: DateTime.UtcNow);

// Structured query with builder
var result = await auditService.QueryAsync(
    new AuditQueryBuilder()
        .WithEventType(AuditEventType.DataRedaction)
        .WithComplianceStandard(ComplianceStandard.Gdpr)
        .WithDateRange(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow));

// Statistics
var stats = await auditService.GetStatisticsAsync(
    from: DateTime.UtcNow.AddDays(-30),
    until: DateTime.UtcNow);

// Export for compliance reporting
var csv = await auditService.ExportAuditAsync(
    from: DateTime.UtcNow.AddDays(-90),
    until: DateTime.UtcNow,
    format: AuditExportFormat.Csv);
```

### AuditServiceOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `BatchSize` | `int` | `100` | Entries per flush batch |
| `FlushInterval` | `TimeSpan` | `30s` | Timer-based flush interval |
| `MaxQueueSize` | `int` | `10000` | Maximum queued entries |
| `EnableCompression` | `bool` | `true` | Compress stored entries |
| `EnableDeduplication` | `bool` | `false` | Deduplicate similar entries |

---

## 5. Configuration

Privacy and security features can be configured through code, `appsettings.json`, or a combination of both.

### Code-based Configuration

```csharp
services.AddPragmaticLogging(logging =>
{
    logging.AddConsole(config =>
    {
        config.Privacy.EnableRedaction = true;
        config.Privacy.RedactionMode = RedactionMode.Standard;
        config.Privacy.ComplianceStandard = ComplianceStandard.Gdpr;
        config.Privacy.SensitivePropertyNames = new[]
        {
            "password", "token", "email", "ssn"
        };
        config.Privacy.EnableAuditTrail = true;
        config.Privacy.DataRetentionPeriod = TimeSpan.FromDays(730);
    });
});
```

### appsettings.json Configuration

```json
{
  "PragmaticLogging": {
    "Privacy": {
      "EnableRedaction": true,
      "EnableSecretDetection": true,
      "ComplianceStandard": "GDPR",
      "SecretDetection": {
        "MinimumSecretLength": 8,
        "MaximumSecretLength": 2048,
        "PrecompilePatterns": true,
        "MinimumConfidenceForRedaction": 0.7,
        "RedactionStyle": "Placeholder",
        "EnableAuditTrail": true,
        "MaxPatternsPerScan": 50
      },
      "DataRedaction": {
        "RedactionPlaceholder": "[REDACTED]",
        "PreserveLengths": false,
        "PreserveJsonStructure": true,
        "SensitivePropertyNames": [
          "password", "token", "apiKey", "secret",
          "email", "ssn", "creditCard"
        ],
        "PropertyNamePatterns": [
          ".*[Pp]assword.*",
          ".*[Ss]ecret.*",
          ".*[Tt]oken.*"
        ],
        "MessageRedactionPatterns": [
          "\\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Z|a-z]{2,}\\b"
        ]
      }
    },
    "Audit": {
      "Enabled": true,
      "BatchSize": 100,
      "FlushIntervalSeconds": 30,
      "StorageType": "FileSystem",
      "PolicyType": "GDPR",
      "StorageOptions": {
        "BasePath": "logs/audit"
      }
    }
  }
}
```

### Per-Provider Privacy

Each provider can have its own `PrivacyConfiguration`, so you can apply aggressive redaction to file logs while keeping full detail in the memory provider for debugging.

```csharp
services.AddPragmaticLogging(logging =>
{
    // Production file: aggressive redaction
    logging.AddFile("logs/app.log", config =>
    {
        config.Privacy.EnableRedaction = true;
        config.Privacy.RedactionMode = RedactionMode.Aggressive;
    });

    // Debug memory: no redaction for troubleshooting
    logging.AddProvider<PragmaticMemoryProvider>(_ =>
    {
        var config = PragmaticMemoryConfiguration.ForTesting();
        config.Privacy.EnableRedaction = false;
        return new PragmaticMemoryProvider("Memory", config);
    });
});
```

---

## Redaction Pipeline

The following diagram shows how a log entry flows through the privacy pipeline before reaching any output provider.

```
Log Entry
  |
  v
[IsEnabled check] --no--> (dropped)
  |
  yes
  v
[Advanced filter chain] --filtered--> (dropped, counter incremented)
  |
  pass
  v
[Context enrichment] -- adds CorrelationId, UserId, etc.
  |
  v
[Structured property filter] -- applies ContextFilterMode
  |
  v
[Data redaction]
  |-- SecretDetector scans message + property values
  |-- PragmaticDataRedactor checks property names + patterns
  |-- Audit trail records each redaction event
  |
  v
[WriteLogCore] -- provider-specific output
```

This pipeline runs inside `PragmaticLoggerProviderBase.WriteLog()`, so every provider -- built-in or custom -- automatically benefits from the full privacy stack.
