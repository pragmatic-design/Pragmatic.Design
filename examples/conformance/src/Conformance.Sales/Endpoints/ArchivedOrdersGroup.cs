using Pragmatic.Endpoints.Attributes;

namespace Conformance.Sales.Endpoints;

/// <summary>
///     A group <b>inside</b> a group: the two prefixes compose.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A generator test that compares the <b>text</b> of what is emitted is not the same as asking
///         a running application for that address — the difference between «the generator writes the
///         right line» and «the route answers». This case does the second.
///     </para>
///     <para>
///         The parent is declared like any other membership, because it is one:
///         <c>[EndpointGroup&lt;OrdersGroup&gt;]</c>.
///     </para>
/// </remarks>
[EndpointGroup("/archived", Tag = "Conformance Archived Orders")]
[EndpointGroup<OrdersGroup>]
public sealed class ArchivedOrdersGroup;
