using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     A plan's reader of one row: <see cref="Text" /> is the entity's reader name (<see cref="PlanReaderKind.Method" />,
///     <see cref="PlanReaderKind.Include" />) or the construction over <c>r</c> and <c>d</c> (<see cref="PlanReaderKind.Lambda" />).
/// </summary>
internal sealed record PlanReaderModel(PlanReaderKind Kind, string Text = "", EquatableArray<IncludeAssignmentModel> Includes = default);
