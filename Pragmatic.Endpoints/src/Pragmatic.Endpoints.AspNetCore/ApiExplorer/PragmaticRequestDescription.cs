namespace Pragmatic.Endpoints.ApiExplorer;

/// <summary>
///     Endpoint metadata: every value a generated endpoint binds from the request.
/// </summary>
/// <remarks>
///     A handler mapped as a <c>Delegate</c> tells ASP.NET this through its <c>MethodInfo</c>. A generated
///     endpoint is mapped as a <c>RequestDelegate</c>, so that it survives an AOT publish, and has none —
///     so the generator, which wrote the binding, attaches what it bound. Its presence is also what marks
///     an endpoint as Pragmatic's to <see cref="PragmaticApiDescriptionProvider" />.
/// </remarks>
/// <param name="parameters">The bound values, in the order the handler declares them.</param>
public sealed class PragmaticRequestDescription(params PragmaticParameterDescription[] parameters)
{
    /// <summary>The bound values, in the order the handler declares them.</summary>
    public IReadOnlyList<PragmaticParameterDescription> Parameters { get; } = parameters;
}
