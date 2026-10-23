namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>An engine's <c>LoadAsync</c>: its session, its predicate's composition, its page type, and whether its count is a long.</summary>
internal sealed record GridLoadModel(string Session, string ToPredicate, string Page, bool CountsInLong);
