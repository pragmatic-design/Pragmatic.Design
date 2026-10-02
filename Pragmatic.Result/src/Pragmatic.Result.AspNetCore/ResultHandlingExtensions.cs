using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Extension methods for registering automatic Result handling.
/// </summary>
public static class ResultHandlingExtensions
{
    /// <summary>
    ///     Adds automatic Result-to-HTTP response conversion for all endpoints in this group.
    /// </summary>
    /// <param name="builder">The route group builder</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    ///     <para>
    ///         When enabled, endpoints that return <see cref="Result{TValue, TError}" /> or
    ///         <see cref="VoidResult{TError}" /> will be automatically converted to HTTP responses:
    ///         <list type="bullet">
    ///             <item>Success with value → 200 OK with JSON body</item>
    ///             <item>Success without value → 204 No Content</item>
    ///             <item>Failure → ProblemDetails with appropriate status code</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         Use <see cref="SkipResultHandlingAttribute" /> to opt-out specific endpoints.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    /// var app = builder.Build();
    /// 
    /// // Apply to all API endpoints
    /// app.MapGroup("").WithResultHandling()
    ///    .MapGet("/users/{id}", async (int id, UserService svc)
    ///        => await svc.GetUserAsync(id))  // Returns Result&lt;User, NotFoundError&gt;
    ///    .MapPost("/users", async (CreateRequest req, UserService svc)
    ///        => await svc.CreateAsync(req)); // Returns Result&lt;User, ConflictError&gt;
    /// </code>
    /// </example>
    public static RouteGroupBuilder WithResultHandling(this RouteGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<ResultEndpointFilter>();
    }

    /// <summary>
    ///     Adds automatic Result-to-HTTP response conversion for this specific endpoint.
    /// </summary>
    /// <param name="builder">The route handler builder</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    ///     Prefer using <see cref="WithResultHandling(RouteGroupBuilder)" /> on a group for consistency.
    /// </remarks>
    public static RouteHandlerBuilder WithResultHandling(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<ResultEndpointFilter>();
    }
}