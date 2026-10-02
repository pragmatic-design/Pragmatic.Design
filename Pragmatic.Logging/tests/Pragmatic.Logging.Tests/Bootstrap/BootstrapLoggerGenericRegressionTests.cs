using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Logging.AspNetCore;
using Xunit;

namespace Pragmatic.Logging.Tests.Bootstrap;

/// <summary>
/// Regression tests for the generic <see cref="BootstrapLogger.CreateLogger{T}"/> overload.
/// After the transition the non-generic logger is a CompositeLogger, so casting it directly
/// to <c>ILogger&lt;T&gt;</c> would throw <see cref="System.InvalidCastException"/>.
/// </summary>
public class BootstrapLoggerGenericRegressionTests
{
    private sealed class SampleService;

    [Fact]
    public void CreateLoggerOfT_AfterTransition_DoesNotThrowAndReturnsTypedLogger()
    {
        using var fullFactory = LoggerFactory.Create(static builder =>
            builder.AddProvider(NullLoggerProvider.Instance));

        // Drive the real transition path, the one a direct cast breaks.
        BootstrapLogger.TransitionToFullLogging(fullFactory);

        // A direct cast would throw InvalidCastException here (CompositeLogger -> ILogger<T>).
        var act = () => BootstrapLogger.CreateLogger<SampleService>();

        var logger = act.Should().NotThrow().Subject;
        logger.Should().NotBeNull();
        logger.Should().BeAssignableTo<ILogger<SampleService>>();

        // The returned logger must be usable without throwing.
        logger.LogInformation("after transition");
    }
}
