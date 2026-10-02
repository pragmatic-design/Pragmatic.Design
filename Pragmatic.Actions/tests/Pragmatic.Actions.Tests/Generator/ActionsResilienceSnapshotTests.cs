using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Snapshot tests for DomainAction + [ResiliencePolicy] source generation.
///     Verifies the invoker wraps ExecuteActionAsync with the resilience pipeline.
/// </summary>
public class ActionsResilienceSnapshotTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Resilience.Attributes;
        using Pragmatic.Result;
        """;

    [Fact]
    public async Task Invoker_WithResiliencePolicy_GeneratesWrappedExecution()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            [ResiliencePolicy("external-api")]
            public partial class PlaceOrder : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGeneratorWithResilience(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    [Fact]
    public async Task Invoker_WithResilienceAndDependencies_GeneratesBothInjections()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public interface IOrderRepository { }

            [DomainAction]
            [ResiliencePolicy("order-policy")]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGeneratorWithResilience(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    [Fact]
    public async Task Invoker_VoidActionWithResilience_GeneratesCorrectly()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            [ResiliencePolicy("void-policy")]
            public partial class CancelOrder : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGeneratorWithResilience(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }
}
