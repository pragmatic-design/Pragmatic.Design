using System.Globalization;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pragmatic.Internationalization.AspNetCore.Middleware;
using Pragmatic.Internationalization.AspNetCore.Providers;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Providers;

/// <summary>
///     Regression tests for <see cref="I18NContextMiddleware"/> (I18N-005 configurable query
///     key, I18N-008 thread-culture restoration).
/// </summary>
[Collection("I18nContext")]
public class I18nContextMiddlewareTests
{
    private static (I18NContextMiddleware Middleware, RequestDelegate Next) BuildMiddleware(Func<HttpContext, Task>? onInvoke = null)
    {
        I18NContextMiddleware middleware = null!;
        Task Next(HttpContext ctx) => onInvoke?.Invoke(ctx) ?? Task.CompletedTask;
        middleware = new I18NContextMiddleware(Next);
        return (middleware, Next);
    }

    private static (I18NConfigResolver Resolver, IOptions<I18NOptions> Options) BuildServices(I18NOptions options)
    {
        var opts = Options.Create(options);
        var resolver = new I18NConfigResolver([new SystemConfigProvider(opts)]);
        return (resolver, opts);
    }

    [Fact]
    public async Task InvokeAsync_CustomQueryStringKey_UsesConfiguredKey()
    {
        // Regression I18N-005: the query-string key is configurable via I18NOptions.QueryStringKey.
        // Arrange — configure a non-default key "lang"
        var options = new I18NOptions
        {
            DefaultUICulture = CultureCode.EnglishUS,
            SupportedCultures = [CultureCode.EnglishUS, CultureCode.FromString("it-IT")],
            QueryStringKey = "lang"
        };
        var (resolver, opts) = BuildServices(options);

        string? observedCulture = null;
        var (middleware, _) = BuildMiddleware(_ =>
        {
            observedCulture = I18NContext.Current.CultureCode;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?lang=it-IT");

        var snapshot = I18NContext.Capture();
        try
        {
            // Act
            await middleware.InvokeAsync(context, resolver, opts);

            // Assert — the configured "lang" key drove the override to Italian
            observedCulture.Should().Be("it-IT");
        }
        finally
        {
            I18NContext.Restore(snapshot);
        }
    }

    [Fact]
    public async Task InvokeAsync_DefaultQueryStringKey_DoesNotReactToOtherKey()
    {
        // I18N-005: when the key is the default "culture", a "?lang=" param is ignored.
        // Arrange
        var options = new I18NOptions
        {
            DefaultUICulture = CultureCode.EnglishUS,
            SupportedCultures = [CultureCode.EnglishUS, CultureCode.FromString("it-IT")]
            // QueryStringKey defaults to "culture"
        };
        var (resolver, opts) = BuildServices(options);

        string? observedCulture = null;
        var (middleware, _) = BuildMiddleware(_ =>
        {
            observedCulture = I18NContext.Current.CultureCode;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?lang=it-IT");

        var snapshot = I18NContext.Capture();
        try
        {
            // Act
            await middleware.InvokeAsync(context, resolver, opts);

            // Assert — "lang" ignored, falls back to default UI culture
            observedCulture.Should().Be("en-US");
        }
        finally
        {
            I18NContext.Restore(snapshot);
        }
    }

    [Fact]
    public async Task InvokeAsync_RestoresThreadCulture_AfterRequest()
    {
        // Regression I18N-008: after the request, the middleware must restore the thread
        // cultures it mutated via SetFromConfig()/SyncThreadCulture(), not leave them leaked.
        // Arrange
        var originalUiCulture = new CultureInfo("fr-FR");
        var originalCulture = new CultureInfo("fr-FR");
        Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        Thread.CurrentThread.CurrentCulture = originalCulture;

        var options = new I18NOptions
        {
            DefaultUICulture = CultureCode.FromString("it-IT"),
            SupportedCultures = [CultureCode.FromString("it-IT"), CultureCode.EnglishUS]
        };
        var (resolver, opts) = BuildServices(options);

        CultureInfo? duringRequestUiCulture = null;
        var (middleware, _) = BuildMiddleware(_ =>
        {
            duringRequestUiCulture = Thread.CurrentThread.CurrentUICulture;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context, resolver, opts);

        // Assert — culture was changed during the request, then restored afterwards
        duringRequestUiCulture!.Name.Should().Be("it-IT");
        Thread.CurrentThread.CurrentUICulture.Name.Should().Be("fr-FR");
        Thread.CurrentThread.CurrentCulture.Name.Should().Be("fr-FR");
    }
}
