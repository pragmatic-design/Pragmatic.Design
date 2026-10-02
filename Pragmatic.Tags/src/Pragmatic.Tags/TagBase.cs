using Pragmatic.Persistence.Entity;

namespace Pragmatic.Tags;

/// <summary>
///     Base class for the shared Tag entity.
///     The SG generates one concrete Tag entity per boundary that uses [HasTags].
///     Tags are unique per (Value, Scope) — deduplication is enforced by the SG-generated config.
/// </summary>
public abstract class TagBase : IEntity
{
    // ── Identity ────────────────────────────────────────────────────────

    /// <summary>Tag unique identifier (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>IEntity implementation — maps to Id.</summary>
    public Guid PersistenceId { get => Id; set => Id = value; }

    // ── Content ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Tag value (normalized). This is the canonical form used for matching.
    ///     Lowercase when <see cref="HasTagsAttribute.CaseSensitive"/> is false.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    ///     Display-friendly version of the tag value. Preserves original casing.
    ///     May differ from <see cref="Value"/> when case-insensitive.
    /// </summary>
    public string DisplayValue { get; set; } = string.Empty;

    // ── Scoping ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Tag scope for namespace isolation. Tags with different scopes are independent sets.
    ///     Null means global scope.
    /// </summary>
    public string? Scope { get; set; }

    // ── Statistics ──────────────────────────────────────────────────────

    /// <summary>
    ///     Number of entities using this tag. Updated on add/remove.
    ///     Useful for tag cloud rendering and cleanup of unused tags.
    /// </summary>
    public int UsageCount { get; set; }

    // ── Audit ───────────────────────────────────────────────────────────

    /// <summary>When this tag was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Who created this tag.</summary>
    public string? CreatedBy { get; set; }
}
