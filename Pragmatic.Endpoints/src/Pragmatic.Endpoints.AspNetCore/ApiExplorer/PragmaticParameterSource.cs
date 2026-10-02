namespace Pragmatic.Endpoints.ApiExplorer;

/// <summary>
///     Where a generated endpoint reads one of its request values from.
/// </summary>
public enum PragmaticParameterSource
{
    /// <summary>A route segment.</summary>
    Route,

    /// <summary>A query-string value, repeated or not.</summary>
    Query,

    /// <summary>A request header.</summary>
    Header,

    /// <summary>A form field.</summary>
    Form,

    /// <summary>An uploaded file.</summary>
    FormFile,

    /// <summary>The request body.</summary>
    Body,
}
