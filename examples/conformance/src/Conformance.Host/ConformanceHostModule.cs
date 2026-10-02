using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;
using Conformance.Catalog;
using Conformance.Sales;
using Conformance.Split;

namespace Conformance.Host;

/// <summary>
///     The topology: three modules, a single database.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately the minimum that holds up a complete HTTP round trip. Every extra piece —
///         messaging, jobs, i18n, cache — is a cell of the matrix that gets added when there is
///         something to demonstrate, not before: a host that switches everything on makes every failure
///         harder to attribute.
///     </para>
///     <para>
///         ⚠️ <b>A single database, by necessity.</b> <c>[ReadAccess&lt;CatalogItem&gt;]</c> is a SQL JOIN
///         mechanism: the two boundaries must sit on the same physical database, otherwise
///         <c>PRAG0706</c> fires. The second database is the next cell — the write that crosses a
///         transactional boundary.
///     </para>
/// </remarks>
[Module]
[Include<SalesModule, ConformanceDatabase>]
[Include<CatalogModule, ConformanceDatabase>]
// A module with TWO boundaries inside, and the normal two-argument form: the host registers each one's
// DbContext. The link is the boundaries the module declares, not the module's name — here they are
// called Ledger and Journal, not «Split».
[Include<SplitModule, ConformanceDatabase>]
[NeedsStep<RoutingStep>]
public sealed class ConformanceHostModule;
