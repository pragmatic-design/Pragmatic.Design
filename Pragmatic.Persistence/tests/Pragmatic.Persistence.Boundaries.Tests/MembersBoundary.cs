using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;

namespace PragmaticBoundaries.Tests;

/// <summary>The members, on the documents engine: its <c>[DataSubject]</c> holds the subject registry's collections in its database.</summary>
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Documents)]
public sealed partial class MembersBoundary;
