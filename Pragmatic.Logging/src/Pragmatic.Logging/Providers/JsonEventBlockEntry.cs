namespace Pragmatic.Logging.Providers;

/// <summary>
///     The event block of a call site's state type and what it was encoded from. Immutable, so that publishing it
///     is one reference write: a reader sees the bytes and their key together, never one without the other.
/// </summary>
internal sealed record JsonEventBlockEntry(int Id, string? Name, string Template, byte[] Bytes);
