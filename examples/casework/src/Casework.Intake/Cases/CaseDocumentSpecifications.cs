namespace Casework.Intake.Entities;

/// <summary>The rules a case's document is read by.</summary>
public static partial class CaseDocumentSpecifications
{
    /// <summary>
    ///     One document of one case, by both ids.
    /// </summary>
    /// <remarks>
    ///     Both, and that is the whole rule: a document is not addressable by its own id alone, because
    ///     <c>CaseDocument</c> is not an <c>ITenantEntity</c> and therefore carries no tenant filter of
    ///     its own. What keeps one organisation off another's documents is the case — read through its
    ///     filtered repository — and this specification tying the document to that case.
    ///     <para>
    ///         ⚠️ <c>PersistenceId</c> and not <c>Id</c>: the row's identity is that column, and <c>Id</c>
    ///         is the property over it, which EF cannot translate — "translation of member 'Id' … failed.
    ///         This commonly occurs when the specified member is unmapped." The expressions the generator
    ///         writes itself say <c>__row.PersistenceId</c> for the same reason.
    ///     </para>
    /// </remarks>
    public static Specification<CaseDocument> OfCase(Guid caseId, Guid documentId)
        => Spec<CaseDocument>.Where(document =>
            document.CaseId == caseId && document.PersistenceId == documentId);
}
