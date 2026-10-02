using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for versioned Execute method detection and version dispatch generation.
/// </summary>
public class ActionVersioningTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Result;
        """;

    [Fact]
    public void Action_WithExecuteV2_GeneratesVersioningProperty()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                public required string Name { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v1"));

                public Task<Result<string, IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v2"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var versioningSource = GetGeneratedSource(result, "Versioning");
        versioningSource.Should().NotBeNull();
        versioningSource.Should().Contain("TargetVersion");
        versioningSource.Should().Contain("(int Major, int Minor)");
        versioningSource.Should().Contain("(1, 0)");
    }

    [Fact]
    public void Action_WithExecuteV2_GeneratesInvokerVersionDispatch()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                public required string Name { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v1"));

                public Task<Result<string, IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v2"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invokerSource = GetGeneratedSource(result, "Invoker");
        invokerSource.Should().NotBeNull();
        invokerSource.Should().Contain("ExecuteActionAsync");
        invokerSource.Should().Contain("action.TargetVersion switch");
        invokerSource.Should().Contain("(2, 0)");
        invokerSource.Should().Contain("action.ExecuteV2(ct)");
        invokerSource.Should().Contain("action.Execute(ct)");
    }

    [Fact]
    public void Action_WithExecuteV3_3_GeneratesCorrectVersionTuple()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v1"));

                public Task<Result<string, IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v2"));

                public Task<Result<string, IError>> ExecuteV3_3(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v3.3"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invokerSource = GetGeneratedSource(result, "Invoker");
        invokerSource.Should().NotBeNull();
        invokerSource.Should().Contain("(2, 0)");
        invokerSource.Should().Contain("(3, 3)");
        invokerSource.Should().Contain("action.ExecuteV2(ct)");
        invokerSource.Should().Contain("action.ExecuteV3_3(ct)");
    }

    [Fact]
    public void Action_WithoutVersionedExecute_DoesNotGenerateVersioning()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var versioningSource = GetGeneratedSource(result, "Versioning");
        versioningSource.Should().BeNull();

        // Invoker should NOT contain ExecuteActionAsync override
        var invokerSource = GetGeneratedSource(result, "Invoker");
        invokerSource.Should().NotBeNull();
        invokerSource.Should().NotContain("ExecuteActionAsync");
        invokerSource.Should().NotContain("TargetVersion");
    }

    [Fact]
    public void VoidAction_WithExecuteV2_GeneratesVoidVersionDispatch()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public partial class CancelOrder : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());

                public Task<VoidResult<IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invokerSource = GetGeneratedSource(result, "Invoker");
        invokerSource.Should().NotBeNull();
        invokerSource.Should().Contain("ExecuteActionAsync");
        invokerSource.Should().Contain("VoidResult");
        invokerSource.Should().Contain("(2, 0)");
        invokerSource.Should().Contain("action.ExecuteV2(ct)");
    }

    [Fact]
    public void Action_WithWrongSignature_IgnoresMethod()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));

                // Wrong signature — takes additional parameter, should be ignored
                public Task<Result<string, IError>> ExecuteV2(string extra, CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v2"));
            }
            """;

        var result = RunGenerator(source);

        // Should not generate versioning since ExecuteV2 has wrong signature
        var versioningSource = GetGeneratedSource(result, "Versioning");
        versioningSource.Should().BeNull();

        var invokerSource = GetGeneratedSource(result, "Invoker");
        invokerSource.Should().NotBeNull();
        invokerSource.Should().NotContain("ExecuteActionAsync");
    }

    [Fact]
    public void Action_WithDependencies_GeneratesVersionDispatchAndDependencies()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public required string Name { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v1"));

                public Task<Result<string, IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("v2"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var invokerSource = GetGeneratedSource(result, "Invoker");
        invokerSource.Should().NotBeNull();

        // Should have both InjectDependencies AND ExecuteActionAsync
        invokerSource.Should().Contain("InjectDependencies");
        invokerSource.Should().Contain("ExecuteActionAsync");
        invokerSource.Should().Contain("IOrderRepository");
    }
}
