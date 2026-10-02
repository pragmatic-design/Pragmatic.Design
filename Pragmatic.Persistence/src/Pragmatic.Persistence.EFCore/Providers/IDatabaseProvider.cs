namespace Pragmatic.Persistence.EFCore.Providers;

/// <summary>
///     Marker interface for database providers.
///     Used as generic constraint in [Database&lt;TProvider&gt;] attribute.
/// </summary>
public interface IDatabaseProvider;