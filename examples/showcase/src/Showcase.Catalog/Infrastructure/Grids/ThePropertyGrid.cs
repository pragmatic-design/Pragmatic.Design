using Pragmatic.Persistence.Query.Attributes;

namespace Showcase.Catalog.Infrastructure.Grids;

/// <summary>
///     The columns the back office's property grid may filter and sort on, and what they are called on
///     the wire.
/// </summary>
/// <remarks>
///     <para>
///         <c>[GridAdapter&lt;T&gt;]</c> generates the translation from a grid framework's own JSON —
///         DevExpress load options here — into LINQ, at compile time. The class is partial and empty on
///         purpose: everything is a declaration, and the <c>Apply</c> the endpoint calls is generated
///         into it.
///     </para>
///     <para>
///         ⚠️ <b>Why the generated adapter and not <c>DevExpressAdapter</c>.</b> The runtime one
///         resolves a field name with <c>Expression.Property</c> — reflection, on the request path, for
///         every filter a client sends — and it reaches <b>any</b> public property of the entity,
///         because a name is all it has. This one is a <c>switch</c> over the columns declared below,
///         written when the application is compiled. Same client contract, and a column nobody declared
///         cannot be reached at all.
///     </para>
///     <para>
///         ⚠️ <b>The adapter takes every public scalar property unless told otherwise</b>, which is the
///         useful default and the reason <c>TenantId</c> has to be taken back: it is the column the row
///         filter uses, it comes from the token and never from the caller, and a grid that let a client
///         filter or sort by it would be handing the multi-tenancy boundary to the front end.
///         <c>[GridExclude]</c> is that subtraction, and with a generated <c>switch</c> it is a boundary
///         rather than a convention.
///     </para>
/// </remarks>
[GridAdapter<Property>(Framework = GridFramework.DevExpress)]
// The name the grid sends, where it differs from the column. A front end says "stars"; the column is
// StarRating, and the alias lives here rather than in a translation somebody writes at each call site.
[GridField("stars", Property = "StarRating")]
// Not filterable and not sortable: a grid header on an address is a full-text search pretending to be
// a column — it scans, it cannot use an index, and it is the first thing to time out on a real
// catalogue. [GridField] refines a column the adapter already found as well as renaming one.
[GridField("address", Filterable = false, Sortable = false)]
[GridExclude("TenantId")]
public partial class ThePropertyGrid;
