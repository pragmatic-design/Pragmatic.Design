using Pragmatic.Mapping.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Dtos;

/// <summary>The third level, for reading and writing.</summary>
/// <remarks>
///     It is the leaf: it carries no navigations, so its write list is empty. It must be emitted all
///     the same, because the parent prefixes it, and an absent list and an empty one cannot be told
///     apart from the call site.
/// </remarks>
[MapFrom<AllocationTag>]
[MapTo<AllocationTag>]
[GenerateProjection]
public partial record AllocationTagDto
{
    /// <summary>The merge key, three levels down as well.</summary>
    public Guid Id { get; init; }

    public string Label { get; init; } = "";

    /// <summary>Read and never written: so nothing overwrites it and it shows what set it.</summary>
    public string Status { get; init; } = "";

    /// <summary>The fourth level: it is what makes the shape four deep.</summary>
    public List<TagNoteDto> Notes { get; init; } = [];
}
