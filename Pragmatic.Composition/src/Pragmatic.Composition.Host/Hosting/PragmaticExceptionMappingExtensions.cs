using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Maps infrastructure exceptions that escape the request pipeline to RFC 9110 problem responses,
///     so a persistence optimistic-concurrency conflict surfaces as HTTP 409 — the documented,
///     retryable contract of <c>[ConcurrencyAware]</c> — instead of an unhandled HTTP 500.
/// </summary>
/// <remarks>
///     <para>
///         Registered as the outermost middleware by the generated <c>PragmaticApp</c> host, before any
///         business pipeline or endpoint runs, so it wraps every downstream request.
///     </para>
///     <para>
///         The concurrency exception is matched by its type <em>name</em>
///         (<c>DbUpdateConcurrencyException</c>) rather than a type reference, so
///         <c>Pragmatic.Composition.Host</c> stays free of an Entity Framework Core dependency.
///     </para>
/// </remarks>
public static class PragmaticExceptionMappingExtensions
{
    private const string ConcurrencyExceptionName = "DbUpdateConcurrencyException";

    /// <summary>
    ///     Adds the Pragmatic infrastructure-exception mapping middleware to the pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder, for chaining.</returns>
    public static IApplicationBuilder UsePragmaticExceptionMapping(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            // A rule the database enforced, already classified into a domain error by the unit of work.
            // Rendered as that error rather than as a 500: [LogicKey] and a relation are declarations,
            // and a caller who violates one deserves to be told which, not a stack trace.
            catch (global::Pragmatic.Persistence.Repository.PersistenceRuleViolationException ex)
                when (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = ex.Error.StatusCode;
                var problem = new ProblemDetails
                {
                    Status = ex.Error.StatusCode,
                    Title = ex.Error.Title,
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                    Detail = ex.Error.Description
                };
                // The code every other error carries on the wire: without it a duplicate key was a 409
                // no client could tell from any other conflict.
                problem.Extensions["code"] = ex.Error.Code;
                // And its own properties, as on every other error path: an "in use" that cannot say what
                // uses the row is no answer at all.
                ex.Error.WriteExtensions(problem.Extensions);
                await context.Response.WriteAsJsonAsync(
                    problem,
                    PragmaticProblemJsonContext.Default.ProblemDetails,
                    contentType: "application/problem+json",
                    context.RequestAborted).ConfigureAwait(false);
            }
            // A grid asked to filter on a field the bridge will not filter on. A 400, because the
            // request is the thing that is wrong and the client can fix it by not naming that field.
            //
            // ⚠️ The detail names the field but never says whether it is unknown or withheld. The
            // difference is exactly what an attacker would probe for — "you may not filter on
            // PasswordHash" confirms the column exists — so the distinction stays in
            // GridFieldRejection for a handler that wants it, and out of the response.
            catch (global::Pragmatic.Persistence.Query.Adapters.GridFieldRejectedException ex)
                when (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "Bad Request",
                        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                        Detail = $"The field '{ex.Field}' cannot be filtered or sorted on."
                    },
                    PragmaticProblemJsonContext.Default.ProblemDetails,
                    contentType: "application/problem+json",
                    context.RequestAborted).ConfigureAwait(false);
            }
            catch (Exception ex) when (!context.Response.HasStarted && IsConcurrencyConflict(ex))
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = "Database Conflict",
                        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                        Detail = "The entity was modified or deleted by another operation. Reload the latest state and retry."
                    },
                    PragmaticProblemJsonContext.Default.ProblemDetails,
                    contentType: "application/problem+json",
                    context.RequestAborted).ConfigureAwait(false);
            }
        });

    // Walk the inner-exception chain: EF wraps/surfaces the concurrency exception at varying depths.
    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name == ConcurrencyExceptionName)
                return true;
        }

        return false;
    }
}
