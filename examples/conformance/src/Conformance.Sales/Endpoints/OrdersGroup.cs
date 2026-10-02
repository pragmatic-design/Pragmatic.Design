using Pragmatic.Endpoints.Attributes;

namespace Conformance.Sales.Endpoints;

/// <summary>
///     The root group: a shared route prefix.
/// </summary>
/// <remarks>
///     Declaring a group and belonging to one are two different sentences, and they carry the same name
///     because they are the same noun in two roles: <c>[EndpointGroup("/prefix")]</c> says «I am a
///     group», <c>[EndpointGroup&lt;T&gt;]</c> says «I am in that group». The arity tells them apart.
/// </remarks>
[EndpointGroup("/api/conformance/orders", Tag = "Conformance Orders")]
public sealed class OrdersGroup;
