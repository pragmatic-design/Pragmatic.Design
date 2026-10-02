namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     The resolver used when the application registers none: localizes nothing, so every error keeps
///     the title and description it was written with.
/// </summary>
internal sealed class NullErrorMessageResolver : IErrorMessageResolver
{
    public static readonly NullErrorMessageResolver Instance = new();

    public string? Resolve(string code, object? context) => null;
}
