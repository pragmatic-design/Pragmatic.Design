namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Where a handler parameter's value comes from, once ASP.NET is no longer the one binding it.
/// </summary>
internal enum BindingSource
{
    /// <summary>A route segment.</summary>
    Route,

    /// <summary>A query-string value.</summary>
    Query,

    /// <summary>A query-string value that may repeat, bound as an array.</summary>
    QueryMany,

    /// <summary>A request header.</summary>
    Header,

    /// <summary>A form field.</summary>
    Form,

    /// <summary>An uploaded file.</summary>
    FormFile,

    /// <summary>The JSON request body.</summary>
    Body,

    /// <summary>Resolved from the request's service provider.</summary>
    Service,

    /// <summary>Resolved from the request's service provider under a key.</summary>
    KeyedService,

    /// <summary>The <c>HttpContext</c> itself.</summary>
    HttpContext,

    /// <summary>The request's cancellation token.</summary>
    CancellationToken,
}

/// <summary>
///     One parameter of a generated handler: how it is declared, and how its value is produced.
/// </summary>
/// <remarks>
///     Not a string: strings are enough only while ASP.NET reads the <c>[From…]</c> attributes and
///     does the binding. Under AOT it cannot, so the same list has to drive two things — the
///     handler's signature and the binding code in front of it — and a string cannot.
/// </remarks>
/// <param name="TypeName">The declared type, fully qualified.</param>
/// <param name="Name">The parameter name, which is also the local the binding assigns.</param>
/// <param name="Source">Where the value comes from.</param>
/// <param name="SourceName">The route/query/header/form key, when the source has one.</param>
/// <param name="Kind">Which <c>RequestBinder</c> overload converts it.</param>
/// <param name="IsRequired">Whether an absent value is an error rather than a default.</param>
/// <param name="DefaultValue">The literal used when the value is absent, if any.</param>
/// <param name="ServiceKey">The key expression, for <see cref="BindingSource.KeyedService" />.</param>
internal sealed record BoundParameter(
    string TypeName,
    string Name,
    BindingSource Source,
    string? SourceName = null,
    Models.BindKind Kind = Models.BindKind.Complex,
    bool IsRequired = true,
    string? DefaultValue = null,
    string? ServiceKey = null);
