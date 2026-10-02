using Invoicing.Billing;
using Invoicing.Registry;
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;

namespace Invoicing.Host;

/// <summary>
///     The topology: two modules, one database, with routing and request cultures.
/// </summary>
/// <remarks>
///     Both modules are named here even though Billing already includes Registry: the host says which
///     database each module lives in, and a module that arrives only as somebody's dependency has not
///     been given one.
/// </remarks>
[Module]
[Include<RegistryModule, AppDatabase>]
[Include<BillingModule, AppDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class InvoicingHostModule;
