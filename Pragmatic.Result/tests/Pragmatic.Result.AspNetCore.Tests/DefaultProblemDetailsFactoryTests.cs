using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Result.AspNetCore.Tests;

/// <summary>
///     Covers <c>DefaultProblemDetailsFactory</c> and the <see cref="IErrorMessageResolver" />
///     path. Exercises the real DI wiring (<c>AddPragmaticResult</c>) rather than the internal type
///     directly.
/// </summary>
public class DefaultProblemDetailsFactoryTests
{
    private static IProblemDetailsFactory DefaultFactory()
    {
        var services = new ServiceCollection();
        services.AddPragmaticResult();
        return services.BuildServiceProvider().GetRequiredService<IProblemDetailsFactory>();
    }

    private static IProblemDetailsFactory FactoryWith(IErrorMessageResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resolver); // pre-registered → AddPragmaticResult()'s TryAdd keeps it
        services.AddPragmaticResult();
        return services.BuildServiceProvider().GetRequiredService<IProblemDetailsFactory>();
    }

    #region Default resolver (NullErrorMessageResolver)

    // Regression for #3/#34: with the default resolver the Detail must fall back to error.Description
    // rather than staying null (wiring AddPragmaticResult() must not be worse than the factoryless path).
    [Fact]
    public void Create_DefaultResolver_DetailFallsBackToDescription()
    {
        var problemDetails = DefaultFactory().Create(new DescribedError());

        problemDetails.Detail.Should().Be("A human-readable description.");
    }

    [Fact]
    public void Create_DefaultResolver_NoDescription_DetailIsNull()
    {
        var problemDetails = DefaultFactory().Create(new TestError("NO_DETAIL", 400));

        problemDetails.Detail.Should().BeNull();
    }

    [Fact]
    public void Create_DefaultResolver_TitleFromErrorTitle()
    {
        var problemDetails = DefaultFactory().Create(new DescribedError());

        problemDetails.Title.Should().Be("Described");
    }

    [Fact]
    public void Create_DefaultResolver_EmptyTitle_FallsBackToDefaultTitle()
    {
        var problemDetails = DefaultFactory().Create(new TestError("TEAPOT", 404));

        problemDetails.Title.Should().Be("Not Found");
    }

    [Fact]
    public void Create_DefaultResolver_AddsCodeExtension()
    {
        var problemDetails = DefaultFactory().Create(new TestError("SOME_CODE", 400));

        problemDetails.Extensions.Should().ContainKey("code");
        problemDetails.Extensions["code"].Should().Be("SOME_CODE");
    }

    #endregion

    #region retryAfter ordering (regression #4)

    // A custom WriteExtensions that writes a bogus "retryAfter" must NOT clobber the typed retryAfter:
    // the factory writes it AFTER WriteExtensions.
    [Fact]
    public void Create_TransientError_RetryAfterNotClobberedByWriteExtensions()
    {
        var problemDetails = DefaultFactory().Create(new TransientClobberError());

        problemDetails.Extensions.Should().ContainKey("retryAfter");
        problemDetails.Extensions["retryAfter"].Should().Be(30);
    }

    [Fact]
    public void StaticFactory_TransientError_RetryAfterNotClobberedByWriteExtensions()
    {
        var problemDetails = ProblemDetailsFactory.Create(new TransientClobberError());

        problemDetails.Extensions.Should().ContainKey("retryAfter");
        problemDetails.Extensions["retryAfter"].Should().Be(30);
    }

    [Fact]
    public void StaticFactory_DetailFallsBackToDescription()
    {
        var problemDetails = ProblemDetailsFactory.Create(new DescribedError());

        problemDetails.Detail.Should().Be("A human-readable description.");
    }

    #endregion

    #region Localizing resolver wins

    [Fact]
    public void Create_LocalizingResolver_LocalizedTitleAndDetailWin()
    {
        var factory = FactoryWith(new FakeResolver("Localized detail", "Localized title"));

        var problemDetails = factory.Create(new DescribedError());

        problemDetails.Title.Should().Be("Localized title");
        problemDetails.Detail.Should().Be("Localized detail");
    }

    [Fact]
    public void Create_LocalizingResolver_EmptyDetail_FallsBackToDescription()
    {
        // Resolver returns empty (not null) → still falls back to Description.
        var factory = FactoryWith(new FakeResolver(detail: "", title: null));

        var problemDetails = factory.Create(new DescribedError());

        problemDetails.Detail.Should().Be("A human-readable description.");
        problemDetails.Title.Should().Be("Described"); // resolver title null → error.Title
    }

    #endregion

    #region Test doubles

    private sealed record TestError(string Code, int StatusCode) : Error
    {
        public override string Code { get; } = Code;
        public override int StatusCode { get; } = StatusCode;
    }

    private sealed record DescribedError : Error
    {
        public override string Code => "DESCRIBED";
        public override int StatusCode => 400;
        public override string Title => "Described";
        public override string? Description => "A human-readable description.";
    }

    private sealed record TransientClobberError : Error
    {
        public override string Code => "TRANSIENT";
        public override int StatusCode => 503;
        public override bool IsTransient => true;
        public override TimeSpan? RetryAfter => TimeSpan.FromSeconds(30);

        // Simulates a hand-written override that (wrongly) writes retryAfter itself.
        public override void WriteExtensions(IDictionary<string, object?> extensions)
            => extensions["retryAfter"] = "BOGUS";
    }

    private sealed class FakeResolver(string? detail, string? title) : IErrorMessageResolver
    {
        public string? Resolve(string code, object? context = null) => detail;
        public string? ResolveTitle(string code, object? context = null) => title;
    }

    #endregion

    /// <summary>
    ///     The resolver reaches the error's own extensions, not only its title and detail:
    ///     a validation failure's words are in its extensions.
    /// </summary>
    [Fact]
    public void Create_HandsTheResolverToTheErrorsExtensions()
    {
        var problemDetails = FactoryWith(new OneWordResolver()).Create(new WordyError());

        problemDetails.Extensions["said"].Should().Be("hello");
    }

    private readonly struct WordyError : IError
    {
        public string Code => "WORDY";
        public int StatusCode => 422;

        public void WriteExtensions(IDictionary<string, object?> extensions, IErrorMessageResolver? resolver)
            => extensions["said"] = resolver?.ResolveKey("greeting");
    }

    private sealed class OneWordResolver : IErrorMessageResolver
    {
        public string? Resolve(string code, object? context = null) => null;

        string? IErrorMessageResolver.ResolveKey(string messageKey, IReadOnlyDictionary<string, object>? parameters)
            => messageKey == "greeting" ? "hello" : null;
    }
}
