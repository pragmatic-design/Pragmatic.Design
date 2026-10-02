namespace Pragmatic.SourceGenerator.Features.I18n.Models;

/// <summary>One constant of <c>{ClassName}Keys</c>: where it is below the class, and the key it holds.</summary>
/// <param name="Path">The member path below the class — <c>Validation.LeaveRequest.EndsBeforeItStarts</c>.</param>
/// <param name="Key">The key — <c>validation.leave_request.ends_before_it_starts</c>.</param>
internal sealed record TranslationKeyConstant(string Path, string Key);
