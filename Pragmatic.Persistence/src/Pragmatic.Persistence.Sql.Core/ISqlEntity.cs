namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Marks a class the SQL layer maps as an entity. It carries nothing: it lets the extension methods
///     that only make sense on an entity (<see cref="SqlPath" /> <c>Then</c>) constrain their receiver, so
///     they show on entities and not on every class. The host repository has <c>IEntity</c> for the same
///     role; this is its stand-in in the spike.
/// </summary>
public interface ISqlEntity;
