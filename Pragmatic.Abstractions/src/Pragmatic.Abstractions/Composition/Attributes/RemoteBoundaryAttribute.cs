namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Host-level: declares that a module's actions should be invoked via HTTP
///     instead of in-process. The SG generates HTTP invokers for all public actions
///     in the remote module and skips local DI registration (boundary extensions,
///     repositories, database) for that module.
/// </summary>
/// <typeparam name="TModule">The module (boundary) type that lives in a separate host.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RemoteBoundaryAttribute<TModule> : Attribute
    where TModule : class
{
    /// <summary>
    ///     Optional base URL override. When null, the URL is resolved from configuration
    ///     at <c>Pragmatic:RemoteBoundaries:{ModuleName}:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
