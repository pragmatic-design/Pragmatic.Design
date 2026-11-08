namespace Pragmatic.Identity;

/// <summary>
///     Finds a <c>[PragmaticUser]</c> entity by the value its match property holds: what the generated <c>{User}Resolver</c> reads through, for the
///     current user and for a key a sign-in holds.
/// </summary>
/// <remarks>
///     One contract for every persistence branch, so the resolver names none of them. The generator implements it where the entity's reads live:
///     over the repository's <c>Query()</c> on EF Core, on the generated read repository on the Sql and documents engines.
/// </remarks>
/// <typeparam name="TUser">The <c>[PragmaticUser]</c> entity.</typeparam>
public interface IUserLookup<TUser> where TUser : class
{
    /// <summary>The user whose match property is <paramref name="identityKey" />, or null when there is none.</summary>
    /// <param name="identityKey">The value the match property is compared with.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<TUser?> FindByIdentityKeyAsync(string identityKey, CancellationToken ct = default);
}
