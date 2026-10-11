using Microsoft.Extensions.Logging;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>One entry as a provider received it.</summary>
internal sealed record RecordedLogEntry(
    LogLevel Level, EventId EventId, string Message, KeyValuePair<string, object?>[] Pairs, Exception? Exception);
