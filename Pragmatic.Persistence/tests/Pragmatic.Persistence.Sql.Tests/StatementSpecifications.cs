using Pragmatic.Specification;

namespace PragmaticEntities.Tests;

/// <summary>Specifications over a set the caller built: a <c>HashSet</c>, as the Showcase's rate import passes one (#581).</summary>
public static partial class StatementSpecifications
{
    public static Specification<Statement> Among(HashSet<Guid> ids) => Spec<Statement>.Where(s => ids.Contains(s.PersistenceId));

    public static Specification<Statement> Coded(HashSet<string> codes) => Spec<Statement>.Where(s => codes.Contains(s.Code));
}
