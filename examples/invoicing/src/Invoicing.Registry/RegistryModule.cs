namespace Invoicing.Registry;

/// <summary>
///     The module the host includes. It depends on nothing: Billing reads Registry, never the other way
///     round, and that is what keeps an issued invoice able to keep its own truth.
/// </summary>
[Module(Name = "Invoicing.Registry", Version = "1.0.0",
    Description = "Organizations (the tenants) and the customers they bill")]
public sealed class RegistryModule;
