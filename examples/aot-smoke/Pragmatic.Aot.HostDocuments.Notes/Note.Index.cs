// ReSharper disable once CheckNamespace
namespace Pragmatic.Aot.Host.Notes.Entities;

/// <summary>
///     An index on the note's kind, on this engine only: what the host's start creates and a write does not, so the smoke can tell that
///     the boundary's documents schema was applied at start (MongoDB creates a collection on its first write, not its indexes).
/// </summary>
[Index(nameof(Kind))]
public partial class Note;
