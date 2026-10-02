using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;
using Showcase.Accounts;
using Showcase.Billing;
using Showcase.Booking;
using Showcase.Catalog;
using Showcase.Host.Distributed;

namespace Showcase.Host.Distributed;

/// <summary>
///     Distributed deployment topology: Billing runs on a separate host.
///     Identical to ShowcaseHostModule except Billing is declared as [RemoteBoundary]
///     instead of [Include].
/// </summary>
[Module]
[Include<AccountsModule, ShowcaseAppDatabase>]
[Include<BookingModule, ShowcaseAppDatabase>]
[Include<CatalogModule, ShowcaseAppDatabase>]
[RemoteBoundary<BillingModule>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class ShowcaseDistributedModule;
