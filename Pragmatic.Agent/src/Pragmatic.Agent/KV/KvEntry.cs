namespace Pragmatic.Agent.KV;

/// <summary>
///     A single entry in the KV store. Immutable — mutations create new instances.
/// </summary>
/// <param name="Key">The key.</param>
/// <param name="Value">The value as stored: ciphertext for <c>secret/</c> keys.</param>
/// <param name="Version">The Lamport version of the write.</param>
/// <param name="UpdatedAt">When the write happened.</param>
/// <param name="Owner">
///     The id of the Agent whose client wrote the entry as its own, or null for a plain entry. The entry
///     lives as long as that client's connection, so it also dies with that Agent.
/// </param>
internal sealed record KvEntry(string Key, string Value, long Version, DateTimeOffset UpdatedAt, string? Owner = null);
