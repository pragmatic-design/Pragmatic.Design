using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Attributes;
using Pragmatic.Logging.Privacy.Audit;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy;

/// <summary>
/// High-performance data redactor with configurable privacy patterns and GDPR compliance.
/// Provides automatic redaction for sensitive data in log entries.
/// </summary>
public sealed class PragmaticDataRedactor : IDataRedactor
{
    private readonly PragmaticDataRedactorConfiguration _configuration;
    private readonly HashSet<string> _sensitivePropertyNames;
    private readonly Regex[] _messagePatterns;
    private readonly PragmaticAuditService? _auditService;
    private readonly SecretDetector? _secretDetector;
    private readonly object _compiledPatternsLock = new();

    // Performance optimization: cache compiled patterns — ConcurrentDictionary for thread safety
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> PatternCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PragmaticDataRedactor"/> class.
    /// </summary>
    /// <param name="configuration">The redaction configuration</param>
    /// <param name="auditService">The audit service for recording redaction events</param>
    /// <param name="secretDetector">Optional secret detector for advanced pattern detection</param>
    public PragmaticDataRedactor(PragmaticDataRedactorConfiguration? configuration = null, PragmaticAuditService? auditService = null, SecretDetector? secretDetector = null)
    {
        _configuration = configuration ?? PragmaticDataRedactorConfiguration.CreateDefault();
        _auditService = auditService;
        _secretDetector = secretDetector;

        // Build sensitive property names set for fast lookup
        _sensitivePropertyNames = new HashSet<string>(
            _configuration.SensitivePropertyNames,
            StringComparer.OrdinalIgnoreCase);

        // Compile message patterns for performance
        _messagePatterns = CompilePatterns(_configuration.MessageRedactionPatterns);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldRedact(string propertyName, PropertyCharacteristics characteristics)
    {
        // Check explicit redaction flag
        if (characteristics.HasFlag(PropertyCharacteristics.Redacted))
            return true;

        // Check against known sensitive property names
        if (_sensitivePropertyNames.Contains(propertyName))
            return true;

        // Check name patterns if configured
        if (_configuration.PropertyNamePatterns?.Length > 0)
        {
            foreach (var pattern in _configuration.PropertyNamePatterns)
            {
                if (GetCompiledPattern(pattern).IsMatch(propertyName))
                    return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public Dictionary<string, object?> RedactProperties(Dictionary<string, object?> properties)
    {
        return RedactProperties(properties, LogLevel.Information, "Unknown", null, null);
    }

    /// <inheritdoc />
    public Dictionary<string, object?> RedactProperties(
        Dictionary<string, object?> properties,
        LogLevel logLevel,
        string categoryName,
        string? userId = null,
        string? correlationId = null)
    {
        if (properties.Count == 0)
            return properties;

        var redacted = new Dictionary<string, object?>(properties.Count);

        foreach (var kvp in properties)
        {
            // For now, assume no explicit characteristics - in full implementation
            // this would be passed from the logging system
            var shouldRedact = ShouldRedact(kvp.Key, PropertyCharacteristics.None);

            if (shouldRedact)
            {
                var originalValue = kvp.Value?.ToString() ?? string.Empty;
                var redactedValue = RedactValue(kvp.Value);
                redacted[kvp.Key] = redactedValue;

                // Additional secret detection for property values if enabled
                if (_secretDetector != null && _configuration.EnableSecretDetection && !string.IsNullOrEmpty(originalValue))
                {
                    var context = new SecretDetectionContext
                    {
                        PropertyName = kvp.Key,
                        IsPropertyValue = true,
                        CorrelationId = correlationId
                    };

                    var detections = _secretDetector.DetectSecretsInProperty(kvp.Key, kvp.Value, context);
                    if (detections.Count > 0)
                    {
                        // Override with secret-aware redaction
                        redacted[kvp.Key] = _secretDetector.RedactSecrets(originalValue, context);
                    }
                }

                // Record redaction in audit trail if configured
                if (_configuration.EnableAuditTrail && _auditService != null)
                {
                    var redactionReason = DetermineRedactionReason(kvp.Key);
                    _auditService.RecordRedaction(
                        logLevel,
                        categoryName,
                        kvp.Key,
                        originalValue.Length,
                        redactionReason,
                        _configuration.ComplianceStandard ?? ComplianceStandard.General,
                        userId,
                        correlationId);
                }
            }
            else
            {
                // Deep redaction for complex objects if enabled
                if (_configuration.EnableDeepRedaction && kvp.Value != null)
                {
                    redacted[kvp.Key] = RedactComplexValue(kvp.Value);
                }
                else
                {
                    redacted[kvp.Key] = kvp.Value;
                }
            }
        }

        return redacted;
    }

    /// <inheritdoc />
    public string RedactMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        var result = message;

        // Apply secret detection first if enabled
        if (_secretDetector != null && _configuration.EnableSecretDetection)
        {
            result = _secretDetector.RedactSecrets(result);
        }

        // Apply traditional pattern-based redaction
        if (_messagePatterns.Length > 0)
        {
            foreach (var pattern in _messagePatterns)
            {
                result = pattern.Replace(result, _configuration.RedactionPlaceholder);
            }
        }

        return result;
    }

    /// <summary>
    /// Redacts a single value based on its type and content.
    /// </summary>
    /// <param name="value">The value to redact</param>
    /// <returns>The redacted value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object? RedactValue(object? value)
    {
        if (value == null)
            return null;

        return value switch
        {
            string str => RedactString(str),
            JsonElement json => RedactJsonElement(json),
            _ => _configuration.RedactionPlaceholder
        };
    }

    /// <summary>
    /// Redacts a string value with length preservation option and secret detection.
    /// </summary>
    /// <param name="value">The string to redact</param>
    /// <returns>The redacted string</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string RedactString(string value)
    {
        // Use secret detector for advanced redaction if available
        if (_secretDetector != null && _configuration.EnableSecretDetection)
        {
            return _secretDetector.RedactSecrets(value);
        }

        if (_configuration.PreserveLengths)
        {
            return new string(_configuration.RedactionChar, Math.Min(value.Length, _configuration.MaxPreservedLength));
        }

        return _configuration.RedactionPlaceholder;
    }

    /// <summary>
    /// Redacts a JSON element while preserving structure.
    /// </summary>
    /// <param name="element">The JSON element to redact</param>
    /// <returns>The redacted placeholder</returns>
    private object RedactJsonElement(JsonElement element)
    {
        if (_configuration.PreserveJsonStructure)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => _configuration.RedactionPlaceholder,
                JsonValueKind.Number => 0,
                JsonValueKind.True or JsonValueKind.False => false,
                JsonValueKind.Object => new Dictionary<string, object> { ["redacted"] = true },
                JsonValueKind.Array => new object[] { _configuration.RedactionPlaceholder },
                _ => _configuration.RedactionPlaceholder
            };
        }

        return _configuration.RedactionPlaceholder;
    }

    /// <summary>
    /// Performs deep redaction on complex objects.
    /// </summary>
    /// <param name="value">The complex value to redact</param>
    /// <returns>The value with deep redaction applied</returns>
    private object? RedactComplexValue(object value)
    {
        // Simple implementation - in production this would use reflection
        // to inspect object properties and their attributes
        if (value is string str)
        {
            // Apply message-level redaction patterns
            return RedactMessage(str);
        }

        // For other complex types, return as-is for now
        // Full implementation would inspect properties recursively
        return value;
    }

    /// <summary>
    /// Compiles redaction patterns for optimal performance.
    /// </summary>
    /// <param name="patterns">The patterns to compile</param>
    /// <returns>Array of compiled regex patterns</returns>
    private static Regex[] CompilePatterns(string[] patterns)
    {
        if (patterns.Length == 0)
            return Array.Empty<Regex>();

        return patterns.Select(GetCompiledPattern).ToArray();
    }

    /// <summary>
    /// Gets a compiled regex pattern from cache or creates a new one.
    /// </summary>
    /// <param name="pattern">The pattern to compile</param>
    /// <returns>The compiled regex</returns>
    private static Regex GetCompiledPattern(string pattern)
    {
        return PatternCache.GetOrAdd(pattern,
            static p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// Determines the reason for redaction based on property name and configuration.
    /// </summary>
    /// <param name="propertyName">The property name</param>
    /// <returns>The redaction reason</returns>
    private RedactionReason DetermineRedactionReason(string propertyName)
    {
        // Check if explicitly in sensitive property names
        if (_sensitivePropertyNames.Contains(propertyName))
        {
            return RedactionReason.MarkedSensitive;
        }

        // Check if matches a pattern
        if (_configuration.PropertyNamePatterns?.Length > 0)
        {
            foreach (var pattern in _configuration.PropertyNamePatterns)
            {
                if (GetCompiledPattern(pattern).IsMatch(propertyName))
                {
                    return RedactionReason.PatternMatch;
                }
            }
        }

        // Must be compliance requirement if we got here
        return RedactionReason.ComplianceRequirement;
    }
}

/// <summary>
/// Configuration for the Pragmatic data redactor.
/// </summary>
public sealed class PragmaticDataRedactorConfiguration
{
    /// <summary>
    /// Gets or sets the placeholder text used for redacted values.
    /// </summary>
    public string RedactionPlaceholder { get; set; } = "[REDACTED]";

    /// <summary>
    /// Gets or sets the character used for length-preserving redaction.
    /// </summary>
    public char RedactionChar { get; set; } = '*';

    /// <summary>
    /// Gets or sets whether to preserve the length of redacted strings.
    /// </summary>
    public bool PreserveLengths { get; set; }

    /// <summary>
    /// Gets or sets the maximum length to preserve for redacted values.
    /// </summary>
    public int MaxPreservedLength { get; set; } = 50;

    /// <summary>
    /// Gets or sets whether to preserve JSON structure when redacting.
    /// </summary>
    public bool PreserveJsonStructure { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to perform deep redaction on complex objects.
    /// </summary>
    public bool EnableDeepRedaction { get; set; }

    /// <summary>
    /// Gets or sets property names that should always be redacted.
    /// </summary>
    public string[] SensitivePropertyNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets regex patterns for property names that should be redacted.
    /// </summary>
    public string[] PropertyNamePatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets regex patterns for redacting content in log messages.
    /// </summary>
    public string[] MessageRedactionPatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets whether to enable audit trail for redaction events.
    /// </summary>
    public bool EnableAuditTrail { get; set; }

    /// <summary>
    /// Gets or sets the compliance standard being followed.
    /// </summary>
    public ComplianceStandard? ComplianceStandard { get; set; }

    /// <summary>
    /// Gets or sets whether to enable advanced secret detection.
    /// </summary>
    public bool EnableSecretDetection { get; set; } = true;

    /// <summary>
    /// Creates a default configuration with common sensitive data patterns.
    /// </summary>
    /// <returns>A default redaction configuration</returns>
    public static PragmaticDataRedactorConfiguration CreateDefault()
    {
        return new PragmaticDataRedactorConfiguration
        {
            SensitivePropertyNames = new[]
            {
                // Common sensitive property names
                "password", "pwd", "secret", "key", "token", "auth", "authorization",
                "credential", "pass", "passphrase", "ssn", "social", "creditcard",
                "cardnumber", "cvv", "pin", "account", "iban", "swift", "email",
                "phonenumber", "phone", "mobile", "address", "zipcode", "postal",
                "birthdate", "dob", "salary", "income", "tax", "vat"
            },

            PropertyNamePatterns = new[]
            {
                @"(?i).*password.*",
                @"(?i).*secret.*",
                @"(?i).*key.*",
                @"(?i).*token.*",
                @"(?i).*auth.*",
                @"(?i).*credential.*",
                @"(?i).*ssn.*",
                @"(?i).*social.*",
                @"(?i).*credit.*",
                @"(?i).*card.*",
                @"(?i).*cvv.*",
                @"(?i).*pin.*",
                @"(?i).*account.*",
                @"(?i).*email.*",
                @"(?i).*phone.*",
                @"(?i).*mobile.*"
            },

            MessageRedactionPatterns = new[]
            {
                // Email addresses
                @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
                
                // Credit card numbers (simple pattern)
                @"\b(?:\d{4}[-\s]?){3}\d{4}\b",
                
                // SSN patterns
                @"\b\d{3}-?\d{2}-?\d{4}\b",
                
                // Phone numbers
                @"\b(?:\+?1[-.\s]?)?\(?[0-9]{3}\)?[-.\s]?[0-9]{3}[-.\s]?[0-9]{4}\b",
                
                // API keys (simple pattern for common formats)
                @"\b[A-Za-z0-9]{32,}\b",
                
                // IPv4 addresses (optional - might be needed for debugging)
                // @"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b"
            },

            PreserveLengths = false,
            PreserveJsonStructure = true,
            EnableDeepRedaction = false
        };
    }

    /// <summary>
    /// Creates a GDPR-compliant configuration with aggressive redaction.
    /// </summary>
    /// <returns>A GDPR-compliant redaction configuration</returns>
    public static PragmaticDataRedactorConfiguration CreateGdprCompliant()
    {
        var config = CreateDefault();

        // More aggressive settings for GDPR compliance
        config.EnableDeepRedaction = true;
        config.PreserveLengths = false; // Don't leak length information
        config.PreserveJsonStructure = false; // Don't leak structure information

        // Additional GDPR-sensitive patterns
        var additionalPatterns = new[]
        {
            @"(?i).*name.*",
            @"(?i).*surname.*",
            @"(?i).*firstname.*",
            @"(?i).*lastname.*",
            @"(?i).*address.*",
            @"(?i).*location.*",
            @"(?i).*birth.*",
            @"(?i).*age.*",
            @"(?i).*gender.*",
            @"(?i).*nationality.*",
            @"(?i).*citizenship.*",
            @"(?i).*passport.*",
            @"(?i).*license.*",
            @"(?i).*identification.*",
            @"(?i).*id$",
            @"(?i).*userid.*",
            @"(?i).*user.*",
            @"(?i).*customer.*",
            @"(?i).*client.*"
        };

        config.PropertyNamePatterns = config.PropertyNamePatterns.Concat(additionalPatterns).ToArray();

        return config;
    }
}