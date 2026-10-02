using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Caching.Extensions;
using Pragmatic.Identity;
using Pragmatic.Integration.Tests.Domain.Actions;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     A <c>[Cacheable]</c> action answers from the cache: the body runs once per input and caller, a
///     failure is never kept, and the permission is asked before the cache is.
/// </summary>
/// <remarks>
///     <para>
///         The attribute's own example is an action, and the generator gave the action
///         <c>ICacheable</c> — but the only reader of that interface was the paged query executor. Every
///         invocation ran the body, with no diagnostic.
///     </para>
///     <para>
///         Through the generated invoker and the generated registrations, not a hand-written invoker:
///         what is measured is that the generator tells the pipeline the action is cacheable.
///     </para>
/// </remarks>
public sealed class ACacheableActionIsReadFromTheCacheTests : IDisposable
{
    private readonly CountingRateSource _rates = new();
    private readonly ServiceProvider _provider;
    private ICurrentUser _caller = new TestCaller("alice", RatePermissions.Read);

    public ACacheableActionIsReadFromTheCacheTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching();
        Pragmatic.Actions.Extensions.ServiceCollectionExtensions.AddPragmaticActions(services);
        PragmaticActionsRegistrationExtensions.AddPragmaticActions(services);
        services.AddSingleton<IRateSource>(_rates);
        services.AddScoped(_ => _caller);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task TheSameInput_RunsTheBodyOnce()
    {
        var first = await ReadAsync("EUR");
        var second = await ReadAsync("EUR");

        _rates.Reads.Should().Be(1, "the second read is the cached answer");
        ValueOf(second).Should().Be(ValueOf(first));
    }

    /// <summary>The control: another input is another entry.</summary>
    [Fact]
    public async Task ADifferentInput_RunsAgain()
    {
        await ReadAsync("EUR");
        await ReadAsync("USD");

        _rates.Reads.Should().Be(2);
    }

    /// <summary>The control: what an invalidation names is read again.</summary>
    [Fact]
    public async Task AfterAnInvalidation_RunsAgain()
    {
        await ReadAsync("EUR");
        await PublishRatesAsync();
        await ReadAsync("EUR");

        _rates.Reads.Should().Be(2, "PublishRatesAction invalidates the tag the read is cached under");
    }

    /// <summary>A failure is an answer for this call only: a transient one would otherwise last the whole duration.</summary>
    [Fact]
    public async Task AFailure_IsNotKept()
    {
        var first = await ReadAsync(ReadRateAction.Unknown);
        await ReadAsync(ReadRateAction.Unknown);

        first.IsFailure.Should().BeTrue();
        _rates.Reads.Should().Be(2);
    }

    /// <summary>The permission is asked before the cache is, so a cached answer is never handed to someone who may not have it.</summary>
    [Fact]
    public async Task ACallerWithoutThePermission_IsRefused_EvenWhenTheAnswerIsCached()
    {
        await ReadAsync("EUR");

        _caller = new TestCaller("mallory");
        var refused = await ReadAsync("EUR");

        refused.IsFailure.Should().BeTrue("the answer is cached, and this caller may not read it");
        _rates.Reads.Should().Be(1);
    }

    /// <summary>
    ///     An action's body is opaque — it may read the caller, or rows filtered for them — so the entry is
    ///     the caller's own.
    /// </summary>
    [Fact]
    public async Task AnotherCaller_DoesNotReadTheFirstCallersEntry()
    {
        await ReadAsync("EUR");

        _caller = new TestCaller("bob", RatePermissions.Read);
        await ReadAsync("EUR");
        await ReadAsync("EUR");

        _rates.Reads.Should().Be(2, "bob gets his own entry, and reads it the second time");
    }

    private async Task<Result<string, IError>> ReadAsync(string currency)
    {
        await using var scope = _provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<ReadRateAction, string>>();
        return await invoker.InvokeAsync(new ReadRateAction { Currency = currency });
    }

    private async Task PublishRatesAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<PublishRatesAction>>();
        var published = await invoker.InvokeAsync(new PublishRatesAction());
        published.IsSuccess.Should().BeTrue();
    }

    private static string ValueOf(Result<string, IError> result)
        => result.IsSuccess ? result.Value : $"<failure {result.Error.Code}>";
}
