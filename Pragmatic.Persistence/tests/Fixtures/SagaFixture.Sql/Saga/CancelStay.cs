namespace PragmaticSagas.Tests;

/// <summary>The confirmation step's compensation, seeded from the saga's state.</summary>
public sealed record CancelStay(Guid StayId);
