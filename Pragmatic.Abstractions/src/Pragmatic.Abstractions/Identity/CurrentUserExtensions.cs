namespace Pragmatic.Identity;

/// <summary>
///     Extension methods for <see cref="ICurrentUser" />.
/// </summary>
public static class CurrentUserExtensions
{
    /// <param name="user">The current user.</param>
    extension(ICurrentUser user)
    {
        /// <summary>
        ///     Returns the user's Id if authenticated, or <c>null</c> if anonymous.
        ///     Useful for populating nullable audit fields (e.g., <c>CreatedBy</c>).
        /// </summary>
        /// <returns>The user's Id when authenticated; otherwise <c>null</c>.</returns>
        public string? IdOrNull()
            => user.IsAuthenticated ? user.Id : null;

        /// <summary>
        ///     Returns the user's display name, falling back to Id if no display name is set.
        /// </summary>
        /// <returns>The display name, or the user's Id when no display name is set.</returns>
        public string DisplayNameOrId()
            => user.DisplayName ?? user.Id;

        /// <summary>
        ///     Gets the first value for a claim type, or <c>null</c> if not present.
        /// </summary>
        /// <param name="type">The claim type to look up.</param>
        /// <returns>The first claim value, or <c>null</c> when the claim is absent.</returns>
        public string? GetClaim(string type)
            => user.Claims.TryGetValue(type, out var values) && values.Count > 0 ? values[0] : null;

        /// <summary>
        ///     Gets all values for a claim type, or an empty list if not present.
        /// </summary>
        /// <param name="type">The claim type to look up.</param>
        /// <returns>All values for the claim, or an empty list when the claim is absent.</returns>
        public IReadOnlyList<string> GetClaims(string type)
            => user.Claims.TryGetValue(type, out var values) ? values : [];
    }
}
