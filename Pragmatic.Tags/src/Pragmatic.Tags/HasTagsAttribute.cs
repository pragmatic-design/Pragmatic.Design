namespace Pragmatic.Tags;

/// <summary>
///     Marks an entity as having an associated tags collection (M:N relationship).
///     This is a <b>Trait (Strada C)</b> — the SG generates the entire domain
///     feature in the consumer, with a junction table for M:N relationship.
///     <para>
///     The SG generates per consumer entity:
///     </para>
///     <list type="bullet">
///         <item><description>A shared <c>Tag</c> entity (once per boundary) with <c>Value</c>, <c>Scope</c>, usage tracking</description></item>
///         <item><description>A junction entity <c>{Parent}Tag</c> with <c>{Parent}Id</c> + <c>TagId</c> composite FK</description></item>
///         <item><description>EF Core configurations for both entities</description></item>
///         <item><description>Actions: <c>Add{Parent}Tag</c>, <c>Remove{Parent}Tag</c></description></item>
///         <item><description>A read side: <c>{Parent}TagDto</c> and the paged <c>List{Parent}TagsQuery</c></description></item>
///         <item><description>REST endpoints under the parent route: <c>POST /api/.../{{parentId}}/tags</c>, <c>GET /api/.../{{parentId}}/tags</c> and <c>DELETE /api/.../{{parentId}}/tags/{{tagId}}</c></description></item>
///         <item><description>A navigation property <c>ICollection&lt;{Parent}Tag&gt; Tags</c> on the parent entity</description></item>
///     </list>
///     <para>
///     The read side projects from the junction, so each row carries the tag itself
///     (<c>Value</c>, <c>DisplayValue</c>, <c>Scope</c>) together with the link audit
///     (<c>AddedAt</c>, <c>AddedBy</c>). The <c>GET</c> endpoint is gated on the
///     <c>{boundary}.{entity}.tags.read</c> permission.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HasTagsAttribute : Attribute
{
    // ── Limits ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Maximum number of tags per entity instance. 0 = unlimited. Default: 50.
    ///     <para>
    ///     The limit holds under concurrency: when it is set, the generated add action runs in a
    ///     serializable transaction, so two simultaneous requests for the last slot cannot both
    ///     succeed. The one the database refuses gets <c>409 Conflict</c> and can simply be retried.
    ///     </para>
    /// </summary>
    public int MaxPerEntity { get; set; } = 50;

    // ── Tag Creation ────────────────────────────────────────────────────

    /// <summary>
    ///     Whether users can create new tags on the fly (free-form tagging).
    ///     When false, tags must be pre-created (curated taxonomy).
    ///     Default: true.
    /// </summary>
    public bool AllowCustom { get; set; } = true;

    /// <summary>
    ///     Whether tag matching is case-sensitive.
    ///     When false, "Urgent" and "urgent" are the same tag.
    ///     Default: false.
    /// </summary>
    public bool CaseSensitive { get; set; }

    // ── Scoping ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Tag namespace scope. Tags with different scopes are independent sets.
    ///     Default: the parent entity type name (e.g. "Reservation").
    ///     Set to a shared value (e.g. "global") for cross-entity tag sharing.
    /// </summary>
    public string? Scope { get; set; }

    // ── Boundary ────────────────────────────────────────────────────────

    /// <summary>
    ///     Override the sub-boundary name in the boundary interface.
    ///     Default: <c>{ParentTypeName}Tags</c> (e.g. "ReservationTags").
    /// </summary>
    public string? SubBoundary { get; set; }
}
