using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests.StaffOnDocuments;

/// <summary>
///     The staff, on the documents engine: the boundary <see cref="StaffOnDocumentsModule" /> imports Pragmatic.Authorization.Management
///     into. Its schema (<c>StaffOnDocumentsBoundaryDocSchema</c>) holds the package's collections beside its own.
/// </summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Documents)]
public sealed partial class StaffOnDocumentsBoundary;
