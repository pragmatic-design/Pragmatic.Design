using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Logging;

/// <summary>
///     A <c>[LoggerMessage]</c> method bound to the Pragmatic attribute gets its body from this generator.
/// </summary>
/// <remarks>
///     A partial method with an accessibility modifier and no implementation does not compile (CS8795), so
///     "the compilation has no errors" is the claim that the body was written, for each method form.
/// </remarks>
public class ACallSiteGetsABodyTests
{
    [Fact]
    public void AStaticMethodTakingTheLogger_Compiles()
    {
        var result = LogCallSiteTestSources.Run("""
            using Microsoft.Extensions.Logging;

            namespace Sample.Orders;

            public static partial class OrderLog
            {
                [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Order {OrderId} placed for {Amount}")]
                public static partial void OrderPlaced(ILogger logger, int orderId, decimal amount);
            }
            """);

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty();
        GeneratorTestHelper.GetGeneratedSource(result, "OrderLog.LogCallSites").Should().Contain("partial void OrderPlaced(");
    }

    [Fact]
    public void AnInstanceMethodUsingALoggerField_Compiles()
    {
        var result = LogCallSiteTestSources.Run("""
            using Microsoft.Extensions.Logging;

            namespace Sample.Orders;

            public sealed partial class OrderService(ILogger<OrderService> logger)
            {
                private readonly ILogger _logger = logger;

                [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} was rejected: {Reason}")]
                private partial void OrderRejected(int orderId, string reason);

                public void Reject(int orderId) => OrderRejected(orderId, "out of stock");
            }
            """);

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty();
    }

    [Fact]
    public void ALevelTakenAsAParameter_AndAnException_Compile()
    {
        var result = LogCallSiteTestSources.Run("""
            using System;
            using Microsoft.Extensions.Logging;

            namespace Sample.Orders;

            public static partial class OrderLog
            {
                [LoggerMessage(EventId = 7, Message = "Payment for {OrderId} failed")]
                public static partial void PaymentFailed(ILogger logger, LogLevel level, Exception error, Guid orderId);
            }
            """);

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty();
    }
}
