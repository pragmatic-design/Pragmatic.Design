namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A collection navigation of a Pragmatic entity, as the generated repository's <c>INavigationLinker</c> names it: a case of
///     <c>LinkAsync</c>, the rows chosen by key read with a compiled read of <see cref="ElementType" /> (a one-to-many's), or stubs (a many-to-many's).
/// </summary>
/// <param name="Name">The navigation (<c>Amenities</c>).</param>
/// <param name="ElementType">The related entity, fully qualified with <c>global::</c>.</param>
internal sealed record LinkNavigationModel(string Name, string ElementType);
