using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;
using Showcase.Accounts;
using Showcase.Billing;
using Showcase.Booking;
using Showcase.Catalog;

namespace Showcase.Host;

/// <summary>
/// Declares the deployment topology for the Showcase host.
///
/// Database split:
///   ShowcaseAppDatabase      → Accounts + Booking + Catalog (operational data, same DB)
///   ShowcaseFinancialDatabase → Billing (financial data, isolated for compliance)
///
/// Accounts imports [UsePackage&lt;LocalIdentityPackage&gt;] at module level.
/// The host is agnostic to the identity provider — it just includes the Accounts module.
/// </summary>
[Module]
[Include<AccountsModule, ShowcaseAppDatabase>]
[Include<BillingModule, ShowcaseFinancialDatabase>]
[Include<BookingModule, ShowcaseAppDatabase>]
[Include<CatalogModule, ShowcaseAppDatabase>]
// Amenity is [Audited], so the trail exists and grows. The retention operation was registered and
// never called by anything — this is what makes it reachable: POST /admin/audit/prune-audit-trail.
[UsePackage<global::Pragmatic.Audit.Management.AuditManagementPackage>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class ShowcaseHostModule;
