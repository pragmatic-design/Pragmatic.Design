using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Caching;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     An action that declares <c>[InvalidatesCache]</c> has its invalidation run after it commits.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The generator emits a correct <c>ICacheInvalidator</c> implementation for any type
///         carrying the attribute — <c>TransformInvalidates</c> accepts any named symbol — so the
///         action invokers have to ask whether the operation is one, as <c>MutationInvoker</c> does.
///         An invoker that does not ask lets an action write its rows while every <c>[Cacheable]</c>
///         read of them keeps answering from the page taken before the write, for the whole cache
///         duration, with no diagnostic and no log.
///     </para>
/// </remarks>
public class AnActionThatInvalidatesCacheTests
{
    /// <summary>The invalidation of an action that returns a value runs, and reaches the stack.</summary>
    [Fact]
    public async Task AnAction_ThatDeclaresInvalidation_InvalidatesAfterItSucceeds()
    {
        var cache = new RecordingCache();
        var provider = BuildProvider(services => services.AddSingleton<ICacheStack>(cache));

        var result = await new PublishRatesInvoker(provider)
            .InvokeAsync(new PublishRatesAction(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        cache.InvalidatedTags.Should().Contain("rates");
    }

    /// <summary>The same for an action that returns nothing: it is the more common writing shape.</summary>
    [Fact]
    public async Task AVoidAction_ThatDeclaresInvalidation_InvalidatesAfterItSucceeds()
    {
        var cache = new RecordingCache();
        var provider = BuildProvider(services => services.AddSingleton<ICacheStack>(cache));

        var result = await new PurgeRatesInvoker(provider)
            .InvokeAsync(new PurgeRatesAction(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        cache.InvalidatedTags.Should().Contain("rates");
    }

    /// <summary>
    ///     The control: a failed action invalidates nothing.
    /// </summary>
    /// <remarks>
    ///     Without it "the invalidation ran" is satisfied by invalidating unconditionally, which would
    ///     throw away a cache entry that is still correct every time an action returns a failure — and
    ///     it would pass the two cases above just as well.
    /// </remarks>
    [Fact]
    public async Task AnAction_ThatFails_InvalidatesNothing()
    {
        var cache = new RecordingCache();
        var provider = BuildProvider(services => services.AddSingleton<ICacheStack>(cache));

        var result = await new RefuseRatesInvoker(provider)
            .InvokeAsync(new RefuseRatesAction(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        cache.InvalidatedTags.Should().BeEmpty("nothing was written, so nothing is stale");
    }

    /// <summary>
    ///     The second control: no <c>ICacheStack</c> registered is survivable, not a thrown failure.
    /// </summary>
    /// <remarks>
    ///     The action declared an invalidation and the deployment has no cache. That is the case
    ///     <c>MutationInvoker</c> logs rather than throws, because the write itself succeeded and the
    ///     commit has already happened — turning it into an exception would make the caller retry an
    ///     operation that is done.
    /// </remarks>
    [Fact]
    public async Task AnAction_WithNoCacheRegistered_StillSucceeds()
    {
        var provider = BuildProvider();

        var result = await new PublishRatesInvoker(provider)
            .InvokeAsync(new PublishRatesAction(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // =========================================================================
    // Test doubles — what the generator emits for [InvalidatesCache], by hand
    // =========================================================================

    private sealed class PublishRatesAction : DomainAction<int>, ICacheInvalidator
    {
        public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<int, IError>.Success(1));

        public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default)
            => cache.InvalidateByTagAsync("rates", ct);
    }

    private sealed class PublishRatesInvoker(IServiceProvider serviceProvider)
        : DomainActionInvoker<PublishRatesAction, int>(serviceProvider)
    {
        protected override void InjectDependencies(PublishRatesAction action) { }
    }

    private sealed class RefuseRatesAction : DomainAction<int>, ICacheInvalidator
    {
        public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<int, IError>.Failure(new RatesRefusedError()));

        public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default)
            => cache.InvalidateByTagAsync("rates", ct);
    }

    private sealed class RefuseRatesInvoker(IServiceProvider serviceProvider)
        : DomainActionInvoker<RefuseRatesAction, int>(serviceProvider)
    {
        protected override void InjectDependencies(RefuseRatesAction action) { }
    }

    private sealed class PurgeRatesAction : VoidDomainAction, ICacheInvalidator
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(VoidResult<IError>.Success());

        public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default)
            => cache.InvalidateByTagAsync("rates", ct);
    }

    private sealed class PurgeRatesInvoker(IServiceProvider serviceProvider)
        : VoidDomainActionInvoker<PurgeRatesAction>(serviceProvider)
    {
        protected override void InjectDependencies(PurgeRatesAction action) { }
    }

    private sealed class RatesRefusedError : IError
    {
        public string Code => "RATES_REFUSED";
        public int StatusCode => 400;
        public string Title => Code;
    }

    /// <summary>Records the tags an invalidation asked for; everything else is inert.</summary>
    private sealed class RecordingCache : ICacheStack
    {
        private readonly List<string> _invalidatedTags = [];

        public IReadOnlyList<string> InvalidatedTags => _invalidatedTags;

        public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
        {
            _invalidatedTags.Add(tag);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
        {
            _invalidatedTags.AddRange(tags);
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => factory(ct);

        public ValueTask<T> GetOrSetAsync<T>(string key,
            Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => Unwrap(factory, ct);

        private static async ValueTask<T> Unwrap<T>(
            Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory, CancellationToken ct)
            => (await factory(ct).ConfigureAwait(false)).Value;

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => new(default(T));

        public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
            => new((false, default));

        public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null,
            CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
