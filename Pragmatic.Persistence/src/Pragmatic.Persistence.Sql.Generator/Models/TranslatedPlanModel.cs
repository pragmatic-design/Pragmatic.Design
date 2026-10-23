using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     The plan of a translated chain or a <c>[Query]</c>: the statement per dialect (no MySQL or Oracle statement where the dialect cannot
///     run it), the parameters' kinds, the binders (<see cref="Binds" />, statements over <c>q</c>, <c>p</c> and <c>d</c>), the reader and the
///     loader as the translation wrote them, and the constructor's options. <see cref="Origin" /> documents the class; <see cref="Tag" /> is
///     the plan's origin at runtime.
/// </summary>
internal sealed record TranslatedPlanModel(
    string ClassName,
    string TypeName,
    bool FileScoped,
    string? Origin,
    EquatableArray<string> Fields,
    string ElementType,
    string Sqlite,
    string PostgreSql,
    string SqlServer,
    string? MySql,
    string? Oracle,
    EquatableArray<string> Kinds,
    EquatableArray<string> Binds,
    string Reader,
    string Load,
    string Tag,
    bool Reversed,
    bool HasShapeColumns,
    EquatableArray<ShapeColumnModel> ShapeColumns,
    PlanFiltersModel? Filters,
    EquatableArray<int> PredicateSlots,
    bool Registered) : PlanSectionModel(ClassName, Registered);
