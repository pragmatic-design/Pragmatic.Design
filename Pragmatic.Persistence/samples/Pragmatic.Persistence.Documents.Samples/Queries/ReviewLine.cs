using Pragmatic.Mapping.Attributes;
using PragmaticDocumentSamples.Entities;

namespace PragmaticDocumentSamples.Queries;

/// <summary>A review's line: the projection's DTO.</summary>
[MapFrom<Review>]
public sealed class ReviewLine
{
    public string Text { get; init; } = "";
}
