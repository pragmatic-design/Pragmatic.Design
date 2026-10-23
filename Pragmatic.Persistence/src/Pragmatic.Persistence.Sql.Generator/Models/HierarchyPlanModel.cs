namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A <c>[GenerateHierarchy]</c> walk's plan: the recursive statement per dialect, the key as its one parameter, the entity's reader.</summary>
internal sealed record HierarchyPlanModel(
    string ClassName,
    string EntityType,
    string Sqlite,
    string PostgreSql,
    string SqlServer,
    string MySql,
    string Oracle,
    string KeyKind,
    string ReaderName,
    string Tag) : PlanSectionModel(ClassName, Registered: true);
