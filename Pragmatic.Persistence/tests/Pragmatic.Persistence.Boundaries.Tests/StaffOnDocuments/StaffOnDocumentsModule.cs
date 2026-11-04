using Pragmatic.Authorization.Management;
using Pragmatic.Composition.Attributes;

namespace PragmaticBoundaries.Tests.StaffOnDocuments;

/// <summary>
///     A module that imports Pragmatic.Authorization.Management into a boundary on the documents engine, with no EF Core referenced: the
///     package's operations read and write through the store of that engine (Pragmatic.Authorization.Management.Engines).
/// </summary>
[Module]
[UsePackage<AuthorizationManagementPackage, StaffOnDocumentsBoundary>]
public sealed class StaffOnDocumentsModule;
