namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A data scope rule of a scoped Pragmatic entity, registered with the bridge: whether the reads' statements carry it.</summary>
internal sealed record ScopeRuleModel(string RuleType, string EntityType, bool Readable);
