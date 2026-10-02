namespace Pragmatic.SourceGenerator.Features.Patch.Models;

/// <summary>Why a <see cref="PatchModel"/> failed validation.</summary>
internal enum InvalidReason
{
    EntityTypeNotFound,
    NotPartial,
    NoProperties
}
