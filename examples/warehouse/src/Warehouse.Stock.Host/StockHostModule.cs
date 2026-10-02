using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;

namespace Warehouse.Stock.Host;

/// <summary>
///     The topology of this process: one module, one database.
/// </summary>
[Module]
[Include<StockModule, StockDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class StockHostModule;
