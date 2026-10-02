using Conformance.Sales.Entities;
using Pragmatic.Mapping.Attributes;

namespace Conformance.Sales.Dtos;

/// <summary>
///     What was written, in the entity's types.
/// </summary>
/// <remarks>
///     The types here are the real ones, not the request's: that is what lets the assertion tell a
///     conversion that happened from a value left at its default. A <c>Guid</c> read as a <c>Guid</c> is
///     either the one sent or <c>Guid.Empty</c>.
/// </remarks>
[MapFrom<ConversionSubject>]
[GenerateProjection]
public partial record ConversionSubjectDto
{
    public Guid Id { get; init; }

    public Guid ExternalId { get; init; }

    public bool IsPriority { get; init; }

    public int Quantity { get; init; }

    public string Code { get; init; } = "";
}
