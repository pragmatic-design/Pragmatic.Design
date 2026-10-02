using Pragmatic.Mapping.Attributes;
using Pragmatic.Mapping.Mutation;
using Pragmatic.Mapping.EFCore.Tests.Entities;

namespace Pragmatic.Mapping.EFCore.Tests.Dtos;

/// <summary>
///     Write shapes for the three-level graph <c>User → Orders → Lines</c>.
/// </summary>
/// <remarks>
///     <para>
///         Separate from <c>UserFullDto</c> and friends, which carry <c>[MapFrom]</c> alone and are
///         read shapes: adding <c>[MapTo]</c> to those would change what the generator emits for the
///         tests already built on them.
///     </para>
///     <para>
///         These exist to answer one question: at the
///         <b>second</b> level of nesting, does an update preserve the children's identity? The first
///         level does — <c>MapOneToMany</c> matches by key — and
///         <c>MutationChildModel</c> carries no <c>Children</c>, so the recursion that Mapping performs
///         when it computes required navigations has no counterpart when it writes.
///     </para>
/// </remarks>
[MapFrom<OrderLine>]
[MapTo<OrderLine>]
public partial record OrderLineWriteDto
{
    public int Id { get; init; }
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

/// <summary>An order and the lines under it — the second level of the graph.</summary>
[MapFrom<Order>]
[MapTo<Order>]
public partial record OrderWriteDto
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public decimal Total { get; init; }

    public List<OrderLineWriteDto> Lines { get; init; } = [];
}

/// <summary>The root of the three-level write shape.</summary>
[MapFrom<User>]
[MapTo<User>]
public partial record UserWriteDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";

    public List<OrderWriteDto> Orders { get; init; } = [];
}

/// <summary>An order whose lines are <c>AddOnly</c>: lines are added and updated, never removed.</summary>
/// <remarks>
///     Here the declaration sits on a nested DTO and the path is the generated one, which is a
///     different road to the same write than calling the helper directly.
/// </remarks>
[MapFrom<Order>]
[MapTo<Order>]
public partial record OrderAddOnlyLinesDto
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public decimal Total { get; init; }

    [CollectionStrategy(CollectionStrategy.AddOnly)]
    public List<OrderLineWriteDto> Lines { get; init; } = [];
}

/// <summary>The root that carries the order with <c>AddOnly</c> lines.</summary>
[MapFrom<User>]
[MapTo<User>]
public partial record UserAddOnlyDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";

    public List<OrderAddOnlyLinesDto> Orders { get; init; } = [];
}

/// <summary>The address, as a write shape.</summary>
[MapFrom<Address>]
[MapTo<Address>]
public partial record AddressWriteDto
{
    public int Id { get; init; }
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
    public string Country { get; init; } = "";
    public string PostalCode { get; init; } = "";
}

/// <summary>A root that carries a <b>single</b> navigation, not a collection.</summary>
/// <remarks>
///     Distinct from <c>UserWithAddressDto</c>, which is read-only. It exercises the
///     <c>MapOneToOne</c> branch through a generated DTO: a <c>null</c> in the DTO leaves the
///     reference alone, reading it as "I am not telling you about this" rather than "remove it".
/// </remarks>
[MapFrom<User>]
[MapTo<User>]
public partial record UserAddressWriteDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";

    public AddressWriteDto? Address { get; init; }
}

/// <summary>
///     Scalars only, to measure how many columns an update really touches.
/// </summary>
/// <remarks>
///     Separate from the others: there must be no collection here, or merging the children blurs what
///     is being counted.
/// </remarks>
[MapFrom<User>]
[MapTo<User>]
public partial record UserScalarsWriteDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
}
