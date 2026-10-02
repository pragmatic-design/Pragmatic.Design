using System.Runtime.CompilerServices;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Privacy;

/// <summary>
/// Extension methods for applying data redaction to log entries.
/// </summary>
public static class RedactionExtensions
{
    /// <param name="logEntry">The log entry to redact</param>
    extension(LogEntry logEntry)
    {
        /// <summary>
        /// Applies redaction to a log entry using the specified redactor.
        /// </summary>
        /// <param name="redactor">The data redactor to use</param>
        /// <returns>A new log entry with redacted content</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogEntry ApplyRedaction(IDataRedactor redactor)
        {
            if (redactor == null)
                return logEntry;

            return new LogEntry
            {
                Timestamp = logEntry.Timestamp,
                LogLevel = logEntry.LogLevel,
                Category = logEntry.Category,
                EventId = logEntry.EventId,
                Message = redactor.RedactMessage(logEntry.Message),
                MessageTemplate = logEntry.MessageTemplate,
                Exception = logEntry.Exception, // Note: Could also redact exception messages
                Properties = redactor.RedactProperties(logEntry.Properties),
                Scopes = RedactScopes(logEntry.Scopes, redactor)
            };
        }

        /// <summary>
        /// Creates a redacted copy of a log entry with minimal allocations.
        /// </summary>
        /// <param name="redactor">The data redactor to use</param>
        /// <param name="redactedEntry">The redacted log entry output</param>
        /// <returns>True if redaction was applied, false if no redaction was needed</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryApplyRedaction(IDataRedactor redactor, out LogEntry redactedEntry)
        {
            if (redactor == null || !RequiresRedaction(logEntry, redactor))
            {
                redactedEntry = logEntry;
                return false;
            }

            redactedEntry = logEntry.ApplyRedaction(redactor);
            return true;
        }

        /// <summary>
        /// Determines if a log entry requires redaction.
        /// </summary>
        /// <param name="redactor">The data redactor</param>
        /// <returns>True if the entry contains data that should be redacted</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RequiresRedaction(IDataRedactor redactor)
        {
            if (redactor == null)
                return false;

            // Quick check: do any properties need redaction? (HasProperties avoids allocating
            // the lazy dictionary on entries that carry no structured data.)
            if (logEntry.HasProperties)
            {
                foreach (var property in logEntry.Properties)
                {
                    if (redactor.ShouldRedact(property.Key, Attributes.PropertyCharacteristics.None))
                    {
                        return true;
                    }
                }
            }

            // Check if message contains patterns that need redaction
            if (!string.IsNullOrEmpty(logEntry.Message))
            {
                var redactedMessage = redactor.RedactMessage(logEntry.Message);
                if (!ReferenceEquals(redactedMessage, logEntry.Message))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Redacts scope information if it contains sensitive data.
    /// </summary>
    /// <param name="scopes">The scopes to redact</param>
    /// <param name="redactor">The data redactor</param>
    /// <returns>Redacted scopes</returns>
    private static IReadOnlyList<KeyValuePair<string, object?>>? RedactScopes(
        IReadOnlyList<KeyValuePair<string, object?>>? scopes,
        IDataRedactor redactor)
    {
        if (scopes == null || scopes.Count == 0)
            return scopes;

        var redacted = new List<KeyValuePair<string, object?>>(scopes.Count);
        bool hasRedaction = false;

        foreach (var scope in scopes)
        {
            if (redactor.ShouldRedact(scope.Key, Attributes.PropertyCharacteristics.None))
            {
                redacted.Add(new KeyValuePair<string, object?>(scope.Key, "[REDACTED]"));
                hasRedaction = true;
            }
            else
            {
                redacted.Add(scope);
            }
        }

        return hasRedaction ? redacted : scopes;
    }
}