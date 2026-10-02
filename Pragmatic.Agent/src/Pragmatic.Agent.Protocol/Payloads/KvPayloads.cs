using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>App → Agent: get a single KV entry.</summary>
public sealed class KvGetPayload
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }
}

/// <summary>App → Agent: set a KV entry (optional CAS via expectedVersion).</summary>
public sealed class KvSetPayload
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>If set, the write only succeeds if current version matches (compare-and-swap).</summary>
    [JsonPropertyName("expectedVersion")]
    public long? ExpectedVersion { get; init; }

    /// <summary>
    ///     The entry lives as long as the connection that wrote it: the Agent deletes it when that client
    ///     disconnects, and the delete gossips like any other. For what describes a running process — the
    ///     address it serves on — and must not outlive it.
    /// </summary>
    [JsonPropertyName("ephemeral")]
    public bool Ephemeral { get; init; }
}

/// <summary>App → Agent: delete a KV entry.</summary>
public sealed class KvDeletePayload
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }
}

/// <summary>App → Agent: get all entries matching a prefix.</summary>
public sealed class KvPrefixPayload
{
    [JsonPropertyName("prefix")]
    public required string Prefix { get; init; }
}

/// <summary>App → Agent: subscribe to changes on a prefix.</summary>
public sealed class KvWatchPayload
{
    [JsonPropertyName("prefix")]
    public required string Prefix { get; init; }
}

/// <summary>Agent → App: KV entry changed notification.</summary>
public sealed class KvChangedPayload
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("version")]
    public long Version { get; init; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; init; }
}

/// <summary>Response payload for KV get operations.</summary>
public sealed class KvEntryPayload
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("version")]
    public long Version { get; init; }

    [JsonPropertyName("found")]
    public bool Found { get; init; }
}

/// <summary>Response payload for KV prefix operations.</summary>
public sealed class KvEntriesPayload
{
    [JsonPropertyName("entries")]
    public required KvEntryPayload[] Entries { get; init; }
}

/// <summary>Response payload for KV set operations.</summary>
public sealed class KvSetResultPayload
{
    [JsonPropertyName("version")]
    public long Version { get; init; }

    [JsonPropertyName("casConflict")]
    public bool CasConflict { get; init; }
}
