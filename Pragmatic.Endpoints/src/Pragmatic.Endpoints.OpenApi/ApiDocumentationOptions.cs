namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     Where the generated host publishes the API contract.
/// </summary>
/// <remarks>
///     Set through <see cref="PragmaticBuilderApiDocumentationExtensions.UseApiDocumentation" />; read by
///     <see cref="ApiDocumentationMapper" /> when the host maps its endpoints.
/// </remarks>
public sealed class ApiDocumentationOptions
{
    /// <summary>
    ///     Whether the document is published in every environment. Default <c>false</c>: Development only.
    /// </summary>
    /// <remarks>
    ///     The interactive reference, when the host has one, stays in Development either way: the
    ///     document is a contract a client may fetch, the reference is a tool for whoever writes the
    ///     application.
    /// </remarks>
    public bool PublishInEveryEnvironment { get; set; }
}
