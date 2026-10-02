using Pragmatic.Mapping.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Dtos;

/// <summary>The fourth level, for reading and writing. It is the leaf.</summary>
[MapFrom<TagNote>]
[MapTo<TagNote>]
[GenerateProjection]
public partial record TagNoteDto
{
    /// <summary>The merge key, four levels down as well.</summary>
    public Guid Id { get; init; }

    public string Text { get; init; } = "";
}
