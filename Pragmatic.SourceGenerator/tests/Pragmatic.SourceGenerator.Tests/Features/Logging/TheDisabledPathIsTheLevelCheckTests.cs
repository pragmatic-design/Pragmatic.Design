using System.Reflection;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Logging;

/// <summary>
///     Below its level a call site costs the level check and nothing else (#175): the method holds the check
///     and a call, and the state is built in a method of its own that is never inlined into it.
/// </summary>
/// <remarks>
///     <para>
///         The JIT inlines a call site into its caller. When the method built its state itself, the inlined
///         body carried the state and the event id as locals, and a struct holding references has to be
///         zeroed in the prologue: on the runner a 0x80-byte frame cleared with AVX stores and a
///         <c>vzeroupper</c> on the way out, on every call, the disabled ones included. Microsoft's method
///         calls a delegate and has none of it.
///     </para>
///     <para>
///         This fixes the shape the effect depends on; the effect itself (the prologue, the timing) is in
///         the disassembly and the benchmarks, which a test cannot read.
///     </para>
/// </remarks>
public class TheDisabledPathIsTheLevelCheckTests
{
    private const string Source = """
        using System;
        using Microsoft.Extensions.Logging;

        namespace Sample.Orders;

        public static partial class OrderLog
        {
            [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Order {OrderId} placed by {Customer}")]
            public static partial void OrderPlaced(ILogger logger, int orderId, string customer);

            [LoggerMessage(EventId = 7, Message = "Payment for {OrderId} failed")]
            public static partial void PaymentFailed(ILogger logger, LogLevel level, Exception error, Guid orderId);
        }
        """;

    [Fact]
    public void TheMethod_BuildsNoState_ItCallsTheStatesWriter()
    {
        var generated = GeneratorTestHelper.GetGeneratedSource(LogCallSiteTestSources.Run(Source), "OrderLog.LogCallSites")!;
        var method = MethodBody(generated, "OrderPlaced(");

        method.Should().NotContain("new __OrderPlacedLogState(");
        method.Should().Contain("__OrderPlacedLogState.Write(");
    }

    [Theory]
    [InlineData("Sample.Orders.OrderLog+__OrderPlacedLogState")]
    [InlineData("Sample.Orders.OrderLog+__PaymentFailedLogState")]
    public void TheWriter_IsNeverInlined(string stateType)
    {
        var writer = new CompiledCallSites(Source).Type(stateType)
            .GetMethod("Write", BindingFlags.Public | BindingFlags.Static);

        writer.Should().NotBeNull();
        (writer!.MethodImplementationFlags & MethodImplAttributes.NoInlining).Should().Be(MethodImplAttributes.NoInlining);
    }

    // The text between the method's signature and the next blank line after its closing brace.
    private static string MethodBody(string generated, string signature)
    {
        var start = generated.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = generated.IndexOf("private readonly struct", start, StringComparison.Ordinal);
        return generated[start..end];
    }
}
