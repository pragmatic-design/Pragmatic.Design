using System.Net;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Hosting;
using Pragmatic.Persistence.Query.Adapters;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     Deterministic coverage for <see cref="PragmaticExceptionMappingExtensions" />: an optimistic-
///     concurrency conflict escaping the pipeline must be mapped to HTTP 409, while unrelated
///     exceptions must propagate untouched.
/// </summary>
public class PragmaticExceptionMappingTests
{
    // A local type whose NAME matches the one the middleware detects, so the test needs no EF Core
    // dependency — exactly the type-name matching the middleware relies on.
    private sealed class DbUpdateConcurrencyException(Exception? inner = null)
        : Exception("concurrency", inner);

    private static RequestDelegate BuildPipeline(RequestDelegate terminal)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UsePragmaticExceptionMapping();
        app.Run(terminal);
        return app.Build();
    }

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
    }

    private static async Task<string> BodyOf(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(context.Response.Body).ReadToEndAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ConcurrencyConflict_IsMappedTo409()
    {
        var pipeline = BuildPipeline(_ => throw new DbUpdateConcurrencyException());
        var context = NewContext();

        await pipeline(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        context.Response.ContentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task NestedConcurrencyConflict_IsMappedTo409()
    {
        // EF surfaces the concurrency exception at varying depths — the inner-exception walk must find it.
        var pipeline = BuildPipeline(_ =>
            throw new InvalidOperationException("wrapper", new DbUpdateConcurrencyException()));
        var context = NewContext();

        await pipeline(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
    }

    /// <summary>A grid clause naming a field the bridge refuses is a 400, not a server fault.</summary>
    /// <remarks>
    ///     The branch existed and nothing asserted it, which is how the case in the conformance suite
    ///     came to say the refusal reached the caller as a 5xx long after it stopped doing so.
    /// </remarks>
    [Fact]
    public async Task ARefusedGridField_IsMappedTo400()
    {
        var pipeline = BuildPipeline(_ => throw new GridFieldRejectedException(
            "Notes", GridFieldRejection.Unknown));
        var context = NewContext();

        await pipeline(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest,
            "the request is what is wrong, and the client fixes it by not naming that field");
        context.Response.ContentType.Should().StartWith("application/problem+json");
        (await BodyOf(context)).Should().Contain("Notes", "the client has to know which field to drop");
    }

    /// <summary>
    ///     A withheld field answers exactly what an unknown one answers — status and body alike.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The control that keeps the case above from being satisfied by a middleware that discloses
    ///     the reason. "You may not filter on PasswordHash" confirms the column exists, which is what a
    ///     prober is after; the distinction stays in <c>GridFieldRejection</c> for a handler that wants
    ///     it, and never reaches the wire.
    /// </remarks>
    [Fact]
    public async Task AWithheldFieldAndAnUnknownOne_AreIndistinguishableOnTheWire()
    {
        var withheldContext = NewContext();
        await BuildPipeline(_ => throw new GridFieldRejectedException(
            "TenantId", GridFieldRejection.Withheld))(withheldContext);

        var unknownContext = NewContext();
        await BuildPipeline(_ => throw new GridFieldRejectedException(
            "TenantId", GridFieldRejection.Unknown))(unknownContext);

        withheldContext.Response.StatusCode.Should().Be(unknownContext.Response.StatusCode);
        (await BodyOf(withheldContext)).Should().Be(await BodyOf(unknownContext),
            "the response cannot say whether the column exists");
    }

    private sealed class DuplicateWorkEmail : Pragmatic.Result.IError
    {
        public string Code => "DB_CONFLICT";
        public int StatusCode => 409;
        public string Title => "Database Conflict";
        public string? Description => "Duplicate value for WorkEmail.";
    }

    /// <summary>
    ///     A rule the database enforced reaches the caller as that error: its status, and
    ///     the code and detail every other error carries.
    /// </summary>
    /// <remarks>
    ///     The branch copied status and title and dropped the code, so a duplicate was a 409 a client
    ///     could not tell from any other conflict.
    /// </remarks>
    [Fact]
    public async Task AViolatedRule_IsTheErrorItWasClassifiedAs_CodeIncluded()
    {
        var pipeline = BuildPipeline(_ => throw new Pragmatic.Persistence.Repository.PersistenceRuleViolationException(
            new DuplicateWorkEmail()));
        var context = NewContext();

        await pipeline(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        var body = await BodyOf(context);
        body.Should().Contain("\"code\":\"DB_CONFLICT\"");
        body.Should().Contain("Duplicate value for WorkEmail.");
    }

    private sealed class KindInUse : Pragmatic.Result.IError
    {
        public string Code => "ENTITY_IN_USE";
        public int StatusCode => 409;
        public string Title => "In use";

        public void WriteExtensions(IDictionary<string, object?> extensions)
        {
            extensions["entityType"] = "AbsenceKind";
            extensions["usedBy"] = "Allowance";
        }
    }

    /// <summary>
    ///     The error's own properties reach the wire too, as on every other error path: an
    ///     "in use" that cannot say what uses the row is the answer this exists to replace.
    /// </summary>
    [Fact]
    public async Task AViolatedRule_CarriesItsOwnExtensions()
    {
        var pipeline = BuildPipeline(_ => throw new Pragmatic.Persistence.Repository.PersistenceRuleViolationException(
            new KindInUse()));
        var context = NewContext();

        await pipeline(context);

        var body = await BodyOf(context);
        body.Should().Contain("\"code\":\"ENTITY_IN_USE\"");
        body.Should().Contain("\"entityType\":\"AbsenceKind\"");
        body.Should().Contain("\"usedBy\":\"Allowance\"");
    }

    [Fact]
    public async Task UnrelatedException_Propagates()
    {
        var pipeline = BuildPipeline(_ => throw new InvalidOperationException("boom"));
        var context = NewContext();

        var act = () => pipeline(context);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task NoException_PassesThroughUnchanged()
    {
        var pipeline = BuildPipeline(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var context = NewContext();

        await pipeline(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
