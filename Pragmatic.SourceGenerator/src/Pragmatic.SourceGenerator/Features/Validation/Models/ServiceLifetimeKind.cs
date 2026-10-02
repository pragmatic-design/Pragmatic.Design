namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     Service lifetime options matching Microsoft.Extensions.DependencyInjection.
/// </summary>
internal enum ServiceLifetimeKind
{
    Singleton = 0,
    Scoped = 1,
    Transient = 2
}
