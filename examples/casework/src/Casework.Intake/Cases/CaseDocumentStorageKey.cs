namespace Casework.Intake.Cases;

/// <summary>
///     Where a case's documents are kept: one place decides it, and nothing composes it by hand.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The container carries the <b>tenant</b>. File storage has no tenant filter and never will
///         — a key composed without the tenant is how one organisation reads another's documents — so
///         the one rule about it lives here rather than at each call site.
///     </para>
///     <para>
///         ⚠️ A <b>key</b>, composed with <c>/</c>, and never a filesystem path. The provider decides how
///         a key becomes a location — local disk joins it onto its root with the platform's separator,
///         a blob store does not have separators at all — and a <c>\</c> written here would be a literal
///         character in a container name on Linux. The gate runs on Windows and CI runs on ubuntu, which
///         is exactly the difference a hand-written separator hides.
///     </para>
///     <para>
///         ⚠️ <b>The file's name is not the application's to choose, and this class does not pretend
///         otherwise.</b> Measured against <c>LocalDiskFileStorage</c>: of the name handed to
///         <c>SaveAsync</c> the store keeps the <em>extension</em> and replaces the rest with a
///         <c>Guid</c> of its own (<c>$"{Guid.NewGuid():N}{ext}"</c>). Which is also why a name from a
///         client cannot become part of an address — the <c>../</c> in it never reaches a path. So the
///         decision this class holds is the <b>container</b>, and the uploaded name travels to the store
///         as itself, for the extension, and onto the row as data.
///     </para>
/// </remarks>
public static class CaseDocumentStorageKey
{
    /// <summary>The container of one case's documents, inside its organisation.</summary>
    public static string ContainerFor(string tenantId, Guid caseId) => $"cases/{tenantId}/{caseId}";
}
