using Invoicing.Registry;

namespace Invoicing.Billing;

/// <summary>
///     The module the host includes, on top of Registry: an invoice is written for a customer Registry
///     keeps, and reads it through Registry's published contract.
/// </summary>
[Module(Name = "Invoicing.Billing", Version = "1.0.0",
    Description = "Invoices, their lines and the payments recorded against them")]
[IncludeModule<RegistryModule>]
public sealed class BillingModule;
