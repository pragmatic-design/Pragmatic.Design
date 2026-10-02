namespace Pragmatic.Agent.KV;

/// <summary>
///     What a delete leaves behind: the version it happened at. A gossiped write not newer than it is a
///     write the delete already superseded, arriving late, and must not bring the key back.
/// </summary>
internal sealed record KvTombstone(string Key, long Version, DateTimeOffset DeletedAt);
