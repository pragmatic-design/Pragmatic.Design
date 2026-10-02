using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;

namespace Warehouse.Shipping.Host;

/// <summary>
///     The topology of this process: one module, one database.
/// </summary>
[Module]
[Include<ShippingModule, ShippingDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class ShippingHostModule;
