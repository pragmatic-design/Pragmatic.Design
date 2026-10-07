namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>A value a <see cref="Customer" /> owns, with a member of its own that must not be logged.</summary>
public sealed record Address(string City, [property: NotLogged] string Street);
