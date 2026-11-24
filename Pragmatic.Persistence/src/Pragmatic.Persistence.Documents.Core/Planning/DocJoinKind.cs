namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>Which documents a join keeps without a match: none (<c>Join</c>), the outer chain's, the inner chain's, or both.</summary>
public enum DocJoinKind
{
    Inner,
    Left,
    Right,
    Full,
}
