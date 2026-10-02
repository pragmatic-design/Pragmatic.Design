namespace Pragmatic.Result.Http;

/// <summary>
///     Represents an authorization error when the user lacks permission.
///     Maps to HTTP 403 Forbidden.
/// </summary>
/// <remarks>
///     <para>
///         Use this when the user is authenticated but lacks the required
///         permissions to perform the requested operation.
///     </para>
///     <para>
///         For authentication failures (not logged in, invalid token),
///         use <see cref="UnauthorizedError" /> instead.
///     </para>
/// </remarks>
public sealed record ForbiddenError : Error
{
    /// <inheritdoc />
    public override string Code => "FORBIDDEN";

    /// <inheritdoc />
    public override int StatusCode => 403;

    /// <inheritdoc />
    public override string Title => "Forbidden";

    /// <summary>
    ///     Gets the resource the user attempted to access.
    /// </summary>
    public string? Resource { get; init; }

    /// <summary>
    ///     Gets the action the user attempted to perform.
    /// </summary>
    public string? Action { get; init; }

    /// <summary>
    ///     Gets the permissions the caller was missing — empty when the refusal names none.
    /// </summary>
    /// <remarks>
    ///     Always a list, even for one permission: the wire carries it as the <c>requiredPermissions</c>
    ///     array, the same shape whichever layer refused (the HTTP policy or the action pipeline), and
    ///     <see cref="PermissionMatch" /> says whether all of them or any one would have been enough.
    /// </remarks>
    public IReadOnlyList<string> RequiredPermissions { get; init; } = [];

    /// <summary>
    ///     Gets whether every permission in <see cref="RequiredPermissions" /> was required, or any one of them.
    /// </summary>
    public PermissionMatch PermissionMatch { get; init; }

    /// <inheritdoc />
    public override string? Description => RequiredPermissions.Count switch
    {
        0 => "The current user is not allowed to perform this operation.",
        1 => $"The current user is missing the '{RequiredPermissions[0]}' permission.",
        _ when PermissionMatch == PermissionMatch.Any =>
            $"The current user has none of the permissions: {string.Join(", ", RequiredPermissions)}.",
        _ => $"The current user is missing the permissions: {string.Join(", ", RequiredPermissions)}.",
    };

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object>? Parameters => BuildParameters();

    private IReadOnlyDictionary<string, object>? BuildParameters()
    {
        var dict = new Dictionary<string, object>();
        if (Resource is not null)
            dict["resource"] = Resource;
        if (Action is not null)
            dict["action"] = Action;
        if (RequiredPermissions.Count > 0)
        {
            dict["permissions"] = string.Join(", ", RequiredPermissions);
            dict["permissionMatch"] = MatchName;
        }
        return dict.Count > 0 ? dict : null;
    }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (Resource is not null) extensions["resource"] = Resource;
        if (Action is not null) extensions["action"] = Action;
        if (RequiredPermissions.Count > 0)
        {
            // An array, not the list itself: what reaches ProblemDetails.Extensions is serialized as
            // object, and a string[] is the shape every serializer path already writes as a JSON array.
            extensions["requiredPermissions"] = RequiredPermissions.ToArray();
            extensions["permissionMatch"] = MatchName;
        }
    }

    private string MatchName => PermissionMatch == PermissionMatch.Any ? "any" : "all";

    /// <summary>Equal by what the refusal says: the list is compared item by item, in order.</summary>
    /// <remarks>A record compares a list by reference, so two refusals naming the same permissions differed.</remarks>
    public bool Equals(ForbiddenError? other) =>
        other is not null
        && base.Equals(other)
        && Resource == other.Resource
        && Action == other.Action
        && PermissionMatch == other.PermissionMatch
        && RequiredPermissions.SequenceEqual(other.RequiredPermissions);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(Resource);
        hash.Add(Action);
        hash.Add(PermissionMatch);
        foreach (var permission in RequiredPermissions)
            hash.Add(permission);
        return hash.ToHashCode();
    }

    /// <summary>
    ///     Creates a ForbiddenError with optional resource and action context.
    /// </summary>
    public static ForbiddenError Create(string? resource = null, string? action = null)
    {
        return new ForbiddenError { Resource = resource, Action = action };
    }

    /// <summary>
    ///     Creates a ForbiddenError for a missing permission.
    /// </summary>
    public static ForbiddenError MissingPermission(string permission, string? resource = null)
    {
        return new ForbiddenError { RequiredPermissions = [permission], Resource = resource };
    }

    /// <summary>Creates a ForbiddenError for several permissions, all or any of which were required.</summary>
    public static ForbiddenError MissingPermissions(IReadOnlyList<string> permissions, PermissionMatch match, string? resource = null)
    {
        return new ForbiddenError { RequiredPermissions = permissions, PermissionMatch = match, Resource = resource };
    }

    /// <summary>
    ///     Creates a ForbiddenError for a denied action.
    /// </summary>
    public static ForbiddenError ActionDenied(string action, string? resource = null)
    {
        return new ForbiddenError { Action = action, Resource = resource };
    }
}