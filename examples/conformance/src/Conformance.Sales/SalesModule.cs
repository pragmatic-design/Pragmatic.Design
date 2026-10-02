using Pragmatic.Composition.Attributes;

namespace Conformance.Sales;

/// <summary>
///     The module the host includes.
/// </summary>
[Module(Name = "Conformance.Sales", Version = "1.0.0",
    Description = "Entities chosen to cover combinations, not to model a domain")]
public sealed class SalesModule;
