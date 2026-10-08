using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.CallSites;

namespace Pragmatic.Logging.Providers;

/// <summary>
///     The constant parts of a generated call site's line, encoded once (<see cref="JsonLineBlock" />): the level
///     and the logger together, and the call site's event and template.
/// </summary>
public sealed partial class PragmaticJsonProvider
{
    // A logger's blocks, one per level, by the identity of its category string: a logger passes the same string
    // on every call, so a hit is a reference comparison. Open addressing, kept at most half full, nothing ever
    // evicted: each category is encoded once per level it logs at, as long as the loggers that hold it live,
    // which ILoggerFactory makes forever.
    private string?[] _loggerBlockOwners = new string?[64];
    private byte[]?[]?[] _loggerBlocks = new byte[]?[64][];
    private int _loggerBlockCount;

    private bool WritesBlocks => !_jsonOptions.Indented;

    // ,"@level":"…","@logger":"…" — or null for a level outside the ones the line names, written field by field.
    // Under the write lock, as every caller is.
    private byte[]? LevelAndLoggerBlock(LogLevel logLevel, string category)
    {
        if ((uint)logLevel > (uint)LogLevel.None)
            return null;

        var byLevel = LoggerBlocks(category);
        return byLevel[(int)logLevel] ??= JsonLineBlock.Strings(
            _jsonOptions, LevelProperty, GetLogLevelString(logLevel), LoggerProperty, category);
    }

    private byte[]?[] LoggerBlocks(string category)
    {
        var mask = _loggerBlockOwners.Length - 1;
        for (var slot = RuntimeHelpers.GetHashCode(category) & mask;; slot = (slot + 1) & mask)
        {
            var owner = _loggerBlockOwners[slot];
            if (ReferenceEquals(owner, category))
                return _loggerBlocks[slot]!;
            if (owner is null)
                return AddLogger(category);
        }
    }

    private byte[]?[] AddLogger(string category)
    {
        if ((_loggerBlockCount + 1) * 2 > _loggerBlockOwners.Length)
            GrowLoggers();

        var byLevel = new byte[]?[(int)LogLevel.None + 1];
        Place(_loggerBlockOwners, _loggerBlocks, category, byLevel);
        _loggerBlockCount++;
        return byLevel;
    }

    private void GrowLoggers()
    {
        var owners = new string?[_loggerBlockOwners.Length * 2];
        var blocks = new byte[]?[owners.Length][];
        for (var i = 0; i < _loggerBlockOwners.Length; i++)
        {
            if (_loggerBlockOwners[i] is { } owner)
                Place(owners, blocks, owner, _loggerBlocks[i]!);
        }

        _loggerBlockOwners = owners;
        _loggerBlocks = blocks;
    }

    private static void Place(string?[] owners, byte[]?[]?[] blocks, string category, byte[]?[] byLevel)
    {
        var mask = owners.Length - 1;
        var slot = RuntimeHelpers.GetHashCode(category) & mask;
        while (owners[slot] is not null)
            slot = (slot + 1) & mask;

        owners[slot] = category;
        blocks[slot] = byLevel;
    }

    private byte[] EventBlock<TState>(EventId eventId, IUtf8LogStateWriter<TState> utf8)
    {
        var template = utf8.Template;
        if (JsonEventBlock<TState>.Entry is { } entry
            && entry.Id == eventId.Id
            && ReferenceEquals(entry.Name, eventId.Name)
            && ReferenceEquals(entry.Template, template))
            return entry.Bytes;

        var bytes = JsonLineBlock.Event(_jsonOptions, EventIdProperty, EventNameProperty, TemplateProperty, eventId, template);
        JsonEventBlock<TState>.Entry = new JsonEventBlockEntry(eventId.Id, eventId.Name, template, bytes);
        return bytes;
    }
}
