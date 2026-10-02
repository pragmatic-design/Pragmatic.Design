using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;

namespace Warehouse.Orders.Host;

/// <summary>
///     The topology of this process: one module, one database.
/// </summary>
[Module]
[Include<OrdersModule, OrdersDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class OrdersHostModule;
