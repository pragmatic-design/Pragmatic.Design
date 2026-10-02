namespace Pragmatic.Notes;

/// <summary>
///     Marks an entity as having internal staff notes (1:N).
///     Simplified version of [HasComments] — no threading, no moderation, no anonymous.
///     All notes are internal (staff-only), never exposed to end users.
///     <para>The SG generates per consumer entity:</para>
///     <list type="bullet">
///         <item><description>A typed child entity <c>{Parent}Note</c> with <c>{Parent}Id</c> FK</description></item>
///         <item><description>EF Core configuration</description></item>
///         <item><description>CRUD actions: Add, GetById, Update (unless <c>AllowEditing</c> is <c>false</c>), Delete</description></item>
///         <item><description>REST endpoints under <c>/api/.../{{parentId}}/notes</c></description></item>
///         <item><description>Navigation property <c>ICollection&lt;{Parent}Note&gt; Notes</c> on parent</description></item>
///     </list>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HasNotesAttribute : Attribute
{
    /// <summary>Maximum length of note content. Default: 4000.</summary>
    public int MaxLength { get; set; } = 4000;

    /// <summary>
    ///     Whether note authors can edit their own notes. Default: true.
    ///     When <c>false</c> the SG generates neither <c>Update{Parent}NoteAction</c>
    ///     nor its <c>PUT</c> endpoint, so editing is not reachable at all.
    /// </summary>
    public bool AllowEditing { get; set; } = true;

    /// <summary>
    ///     Edit window in minutes after creation. <c>-1</c> (the default) means no limit;
    ///     <c>0</c> closes the window immediately, so a note can never be edited after creation.
    /// </summary>
    public int EditWindowMinutes { get; set; } = -1;

    /// <summary>Override sub-boundary name. Default: {ParentTypeName}Notes.</summary>
    public string? SubBoundary { get; set; }
}
