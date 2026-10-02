using Pragmatic.Mapping.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Dtos;

/// <summary>The grandchild, for reading and writing.</summary>
[MapFrom<Allocation>]
[MapTo<Allocation>]
[GenerateProjection]
public partial record AllocationDto
{
    /// <summary>The merge key, two levels down as well.</summary>
    public Guid Id { get; init; }

    public string Warehouse { get; init; } = "";

    public int Quantity { get; init; }

    /// <summary>
    ///     The value object, carried by type and not by DTO.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A <c>[ValueObject]</c> has no mapping attributes, so Mapping does not treat it as a nested
    ///     DTO: it is a direct assignment of the same type, for reading and writing. It is intended that
    ///     the DTO names the domain type — a value object is immutable and compared by value, so there is
    ///     nothing to adapt.
    /// </remarks>
    public StorageSlot Slot { get; init; } = new("", 0);

    /// <summary>The third level. It is what makes the shape three deep.</summary>
    public List<AllocationTagDto> Tags { get; init; } = [];
}
