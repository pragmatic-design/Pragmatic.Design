using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Pragmatic.Logging.Privacy.Audit;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy;

/// <summary>
/// Advanced secret detection engine that scans log content for sensitive information.
/// </summary>
public sealed class SecretDetector : IDisposable
{
    private readonly SecretDetectionOptions _options;
    private readonly PragmaticAuditService? _auditService;
    private readonly ConcurrentDictionary<string, Regex> _compiledPatterns = new();
    private readonly Dictionary<string, SecretPatternInfo> _patternMetadata;
    private volatile bool _disposed;

    public SecretDetector(SecretDetectionOptions? options = null, PragmaticAuditService? auditService = null)
    {
        _options = options ?? SecretDetectionOptions.CreateDefault();
        _auditService = auditService;
        _patternMetadata = BuildPatternMetadata();

        // Pre-compile critical patterns for performance
        if (_options.PrecompilePatterns)
        {
            PrecompileCriticalPatterns();
        }
    }

    /// <summary>
    /// Detects secrets in the provided text content.
    /// </summary>
    /// <param name="content">The content to scan</param>
    /// <param name="context">Optional context for better detection accuracy</param>
    /// <returns>List of detected secrets</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public List<SecretDetectionResult> DetectSecrets(string content, SecretDetectionContext? context = null)
    {
        if (_disposed || string.IsNullOrEmpty(content) || content.Length < _options.MinimumSecretLength)
            return new List<SecretDetectionResult>();

        var results = new List<SecretDetectionResult>();
        var patternsToCheck = DetermineRelevantPatterns(context);

        foreach (var patternInfo in patternsToCheck)
        {
            try
            {
                var regex = GetCompiledPattern(patternInfo.Pattern);
                var matches = regex.Matches(content);

                foreach (Match match in matches)
                {
                    if (match.Success && ShouldIncludeMatch(match, patternInfo, context))
                    {
                        var detection = CreateDetectionResult(match, patternInfo, context);
                        results.Add(detection);

                        // Record in audit trail if configured
                        if (_options.EnableAuditTrail && _auditService != null)
                        {
                            RecordSecretDetection(detection, context);
                        }
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Pattern took too long — skip it to prevent DoS, but do NOT fail silently: a timed-out
                // pattern means this content may still contain an UNREDACTED secret. Surface it through
                // the audit trail (no ILogger is injected into this type).
                if (_options.EnableAuditTrail && _auditService != null)
                {
                    _auditService.RecordComplianceViolation(
                        violationType: "SecretScanTimeout",
                        description: $"Secret pattern '{patternInfo.Name}' (category {patternInfo.Category}) timed out; " +
                                     "scanned content may contain an unredacted secret.",
                        complianceStandard: ComplianceStandard.General,
                        severity: AuditSeverity.High,
                        correlationId: context?.CorrelationId);
                }

                continue;
            }
            catch (Exception)
            {
                // Continue with other patterns if one fails
                continue;
            }
        }

        // Post-process results to remove duplicates and false positives
        return PostProcessResults(results);
    }

    /// <summary>
    /// Detects secrets in a property value based on property name hints.
    /// </summary>
    /// <param name="propertyName">The name of the property</param>
    /// <param name="propertyValue">The value to check</param>
    /// <param name="context">Optional context</param>
    /// <returns>List of detected secrets</returns>
    public List<SecretDetectionResult> DetectSecretsInProperty(
        string propertyName,
        object? propertyValue,
        SecretDetectionContext? context = null)
    {
        if (_disposed || propertyValue == null)
            return new List<SecretDetectionResult>();

        var content = propertyValue.ToString();
        if (string.IsNullOrEmpty(content))
            return new List<SecretDetectionResult>();

        // Create a local copy so we never mutate the caller's context instance
        var enhancedContext = new SecretDetectionContext
        {
            PropertyName = propertyName,
            IsPropertyValue = true,
            CorrelationId = context?.CorrelationId
        };

        // Check if property name indicates a secret
        if (IsSecretPropertyName(propertyName))
        {
            enhancedContext.PropertyNameIndicatesSecret = true;
            enhancedContext.ConfidenceBoost = 0.3; // Boost confidence for known secret properties
        }

        return DetectSecrets(content, enhancedContext);
    }

    /// <summary>
    /// Redacts detected secrets from the provided content.
    /// </summary>
    /// <param name="content">The content to redact</param>
    /// <param name="context">Optional context</param>
    /// <returns>Content with secrets redacted</returns>
    public string RedactSecrets(string content, SecretDetectionContext? context = null)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var detections = DetectSecrets(content, context);
        if (detections.Count == 0)
            return content;

        // Sort by position descending to avoid offset issues
        var sortedDetections = detections
            .Where(d => d.ShouldRedact)
            .OrderByDescending(d => d.StartPosition)
            .ToList();

        var redactedContent = content;

        foreach (var detection in sortedDetections)
        {
            var replacement = CreateRedactionReplacement(detection);
            redactedContent = redactedContent.Remove(detection.StartPosition, detection.Length)
                                           .Insert(detection.StartPosition, replacement);
        }

        return redactedContent;
    }

    /// <summary>
    /// Gets statistics about secret detection patterns and performance.
    /// </summary>
    /// <returns>Detection statistics</returns>
    public SecretDetectionStatistics GetStatistics()
    {
        return new SecretDetectionStatistics
        {
            CompiledPatternsCount = _compiledPatterns.Count,
            TotalPatternsAvailable = _patternMetadata.Count,
            PrecompiledPatterns = _options.PrecompilePatterns,
            CacheSize = _compiledPatterns.Count,
            Options = _options
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _compiledPatterns.Clear();
        }
    }

    private Dictionary<string, SecretPatternInfo> BuildPatternMetadata()
    {
        var metadata = new Dictionary<string, SecretPatternInfo>();
        var allPatterns = SecretPatterns.GetAllPatterns();

        foreach (var category in allPatterns)
        {
            foreach (var pattern in category.Value)
            {
                var severity = DeterminePatternSeverity(category.Key, pattern.Key);
                metadata[pattern.Key] = new SecretPatternInfo
                {
                    Name = pattern.Key,
                    Category = category.Key,
                    Pattern = pattern.Value,
                    Severity = severity,
                    Confidence = DeterminePatternConfidence(category.Key, pattern.Key)
                };
            }
        }

        return metadata;
    }

    private void PrecompileCriticalPatterns()
    {
        var criticalPatterns = SecretPatterns.GetCriticalPatterns();
        foreach (var pattern in criticalPatterns)
        {
            _ = GetCompiledPattern(pattern);
        }
    }

    private List<SecretPatternInfo> DetermineRelevantPatterns(SecretDetectionContext? context)
    {
        var patterns = _patternMetadata.Values.ToList();

        // Filter based on context
        if (context != null)
        {
            if (context.OnlyCriticalPatterns)
            {
                patterns = patterns.Where(p => p.Severity >= SecretSeverity.High).ToList();
            }

            if (!string.IsNullOrEmpty(context.PropertyName) && context.IsPropertyValue)
            {
                // Prioritize patterns likely to match the property name
                var propertyName = context.PropertyName.ToLowerInvariant();
                patterns = patterns.OrderByDescending(p => GetPatternRelevanceScore(p, propertyName)).ToList();
            }
        }

        // Apply performance limits — but NEVER drop high-severity patterns. A private-key/Crypto pattern
        // cut by the cap would leave that secret unredacted. Keep all critical patterns, then fill the
        // remaining budget with the highest-relevance non-critical ones.
        if (_options.MaxPatternsPerScan > 0 && patterns.Count > _options.MaxPatternsPerScan)
        {
            var critical = patterns.Where(p => p.Severity >= SecretSeverity.High).ToList();
            var rest = patterns.Where(p => p.Severity < SecretSeverity.High);
            patterns = critical
                .Concat(rest)
                .Take(Math.Max(_options.MaxPatternsPerScan, critical.Count))
                .ToList();
        }

        return patterns;
    }

    private double GetPatternRelevanceScore(SecretPatternInfo pattern, string propertyName)
    {
        var score = pattern.Confidence;

        // Boost score based on category-property name correlation
        if (propertyName.Contains("password") && pattern.Category == "Database")
            score += 0.5;
        if (propertyName.Contains("token") && pattern.Category == "Tokens")
            score += 0.5;
        if (propertyName.Contains("key") && pattern.Category == "ApiKeys")
            score += 0.5;
        if (propertyName.Contains("key") && pattern.Category == "Crypto")
            score += 0.3;
        if (propertyName.Contains("connection") && pattern.Category == "Database")
            score += 0.4;

        return score;
    }

    private Regex GetCompiledPattern(string pattern)
    {
        return _compiledPatterns.GetOrAdd(pattern, p =>
        {
            var options = RegexOptions.Compiled | RegexOptions.IgnoreCase;
            if (_options.EnableMultilineMatching)
                options |= RegexOptions.Singleline;

            return new Regex(p, options, TimeSpan.FromSeconds(_options.RegexTimeoutSeconds));
        });
    }

    private bool ShouldIncludeMatch(Match match, SecretPatternInfo patternInfo, SecretDetectionContext? context)
    {
        // Basic length checks
        if (match.Length < _options.MinimumSecretLength || match.Length > _options.MaximumSecretLength)
            return false;

        // Skip matches that look like placeholders or examples
        var matchValue = match.Value.ToLowerInvariant();
        if (_options.SkipPlaceholders && IsPlaceholder(matchValue))
            return false;

        // Check for false positive patterns
        if (_options.SkipFalsePositives && IsFalsePositive(matchValue, patternInfo))
            return false;

        return true;
    }

    private bool IsPlaceholder(string value)
    {
        var placeholderPatterns = new[]
        {
            "your_", "my_", "example_", "test_", "demo_", "sample_",
            "placeholder", "xxxxxxxxxx", "000000", "123456", "abcdef",
            "fake", "dummy", "mock"
        };

        return placeholderPatterns.Any(pattern => value.Contains(pattern));
    }

    private bool IsFalsePositive(string value, SecretPatternInfo patternInfo)
    {
        // Pattern-specific false positive checks
        return patternInfo.Name switch
        {
            "GuidPattern" => IsCommonGuid(value),
            "Base64Key" => IsCommonBase64(value),
            "EncryptionKey" => IsSequentialHex(value),
            _ => false
        };
    }

    private static bool IsCommonGuid(string value)
    {
        var commonGuids = new[]
        {
            "00000000-0000-0000-0000-000000000000",
            "11111111-1111-1111-1111-111111111111"
        };
        return commonGuids.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCommonBase64(string value)
    {
        // Check for repetitive patterns that might not be real keys
        if (value.Length < 20)
            return true;

        var uniqueChars = value.Distinct().Count();
        return uniqueChars < 10; // Too few unique characters for a real key
    }

    private static bool IsSequentialHex(string value)
    {
        // Check for patterns like "123456789abcdef" or "000000000"
        if (value.Length < 16)
            return true;

        var isSequential = true;
        for (int i = 1; i < Math.Min(value.Length, 8); i++)
        {
            if (value[i] != value[i - 1] + 1)
            {
                isSequential = false;
                break;
            }
        }

        return isSequential || value.Distinct().Count() < 4;
    }

    private SecretDetectionResult CreateDetectionResult(
        Match match,
        SecretPatternInfo patternInfo,
        SecretDetectionContext? context)
    {
        var confidence = patternInfo.Confidence;
        if (context?.ConfidenceBoost > 0)
            confidence = Math.Min(1.0, confidence + context.ConfidenceBoost);

        return new SecretDetectionResult
        {
            SecretType = patternInfo.Name,
            Category = patternInfo.Category,
            StartPosition = match.Index,
            Length = match.Length,
            Confidence = confidence,
            Severity = patternInfo.Severity,
            Context = context?.PropertyName,
            ShouldRedact = confidence >= _options.MinimumConfidenceForRedaction
        };
    }

    private List<SecretDetectionResult> PostProcessResults(List<SecretDetectionResult> results)
    {
        if (results.Count <= 1)
            return results;

        // Remove overlapping detections, keeping the one with higher confidence
        var processed = new List<SecretDetectionResult>();
        var sortedResults = results.OrderBy(r => r.StartPosition).ToList();

        foreach (var result in sortedResults)
        {
            var isOverlapping = processed.Any(p =>
                DoesOverlap(p.StartPosition, p.Length, result.StartPosition, result.Length));

            if (!isOverlapping)
            {
                processed.Add(result);
            }
            else
            {
                // Replace if this result has higher confidence
                var overlapping = processed.First(p =>
                    DoesOverlap(p.StartPosition, p.Length, result.StartPosition, result.Length));

                if (result.Confidence > overlapping.Confidence)
                {
                    processed.Remove(overlapping);
                    processed.Add(result);
                }
            }
        }

        return processed;
    }

    private static bool DoesOverlap(int start1, int length1, int start2, int length2)
    {
        var end1 = start1 + length1;
        var end2 = start2 + length2;
        return start1 < end2 && start2 < end1;
    }

    private string CreateRedactionReplacement(SecretDetectionResult detection)
    {
        return _options.RedactionStyle switch
        {
            SecretRedactionStyle.Placeholder => $"[{detection.Category.ToUpperInvariant()}_REDACTED]",
            SecretRedactionStyle.PreserveLength => new string('*', detection.Length),
            SecretRedactionStyle.PreserveStructure => PreserveStructure(detection),
            SecretRedactionStyle.Minimal => "[REDACTED]",
            _ => "[SECRET_REDACTED]"
        };
    }

    private string PreserveStructure(SecretDetectionResult detection)
    {
        // For structured secrets like JWT, preserve the structure
        if (detection.SecretType == "Jwt")
        {
            return "[JWT_HEADER].[JWT_PAYLOAD].[JWT_SIGNATURE]";
        }

        if (detection is { Category: "Database", Length: > 20 })
        {
            return "[DATABASE_PASSWORD_REDACTED]";
        }

        return $"[{detection.Category.ToUpperInvariant()}_REDACTED]";
    }

    private bool IsSecretPropertyName(string propertyName)
    {
        var secretPatterns = SecretPatterns.GetSecretPropertyPatterns();
        return secretPatterns.Any(pattern =>
        {
            var regex = GetCompiledPattern(pattern);
            return regex.IsMatch(propertyName);
        });
    }

    private void RecordSecretDetection(SecretDetectionResult detection, SecretDetectionContext? context)
    {
        _auditService?.RecordComplianceViolation(
            violationType: "SecretExposure",
            description: $"Secret detected in logs: {detection.SecretType} (Category: {detection.Category}, Confidence: {detection.Confidence:P0})",
            complianceStandard: ComplianceStandard.General,
            severity: MapSeverityToAudit(detection.Severity),
            correlationId: context?.CorrelationId
        );
    }

    private static AuditSeverity MapSeverityToAudit(SecretSeverity severity)
    {
        return severity switch
        {
            SecretSeverity.Critical => AuditSeverity.Critical,
            SecretSeverity.High => AuditSeverity.High,
            SecretSeverity.Medium => AuditSeverity.Medium,
            SecretSeverity.Low => AuditSeverity.Low,
            _ => AuditSeverity.Medium
        };
    }

    private static SecretSeverity DeterminePatternSeverity(string category, string patternName)
    {
        return category switch
        {
            "Crypto" => SecretSeverity.Critical,
            "ApiKeys" when patternName.Contains("Aws") => SecretSeverity.Critical,
            "ApiKeys" => SecretSeverity.High,
            "Tokens" when patternName == "Jwt" => SecretSeverity.High,
            "Tokens" => SecretSeverity.Medium,
            "Database" => SecretSeverity.High,
            "Cloud" => SecretSeverity.High,
            "Application" when patternName.Contains("Machine") => SecretSeverity.High,
            "Application" => SecretSeverity.Medium,
            _ => SecretSeverity.Medium
        };
    }

    private static double DeterminePatternConfidence(string category, string patternName)
    {
        return category switch
        {
            "Crypto" => 0.95, // Private keys are very distinctive
            "ApiKeys" when patternName.Contains("Aws") => 0.90,
            "ApiKeys" when patternName.Contains("GitHub") => 0.85,
            "ApiKeys" => 0.75,
            "Tokens" when patternName == "Jwt" => 0.90,
            "Database" => 0.80,
            "Cloud" => 0.80,
            _ => 0.70
        };
    }
}
