// Pragmatic.Actions.Tests - Generator Snapshot Tests
// Snapshot tests for ActionsSourceGenerator output verification.
// Each test runs the generator on a source string and verifies the exact generated output.

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Snapshot-based tests for the ActionsSourceGenerator.
///     These tests verify the exact generated output for all 4 templates:
///     SetDependencies, Invoker, ActionsRegistration, and ActionsMetadata.
/// </summary>
public class ActionsGeneratorSnapshotTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Result;
        """;

    #region SetDependencies Snapshots

    /// <summary>
    ///     Verifies the generated SetDependencies for a single dependency.
    /// </summary>
    [Fact]
    public async Task SetDependencies_SingleDependency_GeneratesCorrectly()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated SetDependencies for multiple dependencies.
    /// </summary>
    [Fact]
    public async Task SetDependencies_MultipleDependencies_GeneratesAllParameters()
    {
        var source = CommonUsings + """
            using Microsoft.Extensions.Logging;

            namespace TestApp.Orders;

            public interface IOrderRepository { }
            public interface IOrderService { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;
                private ILogger<PlaceOrder> _logger;
                private IOrderService _orderService;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies that no SetDependencies is generated when there are no dependencies.
    /// </summary>
    [Fact]
    public async Task SetDependencies_NoDependencies_NoSetDependenciesGenerated()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public partial class SimpleAction : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        // Should only have Invoker and Registration, no SetDependencies
        var generated = GetGeneratedSource(result, "SetDependencies");
        generated.Should().BeNull("no SetDependencies should be generated when there are no dependencies");

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies SetDependencies for a VoidDomainAction with a dependency.
    /// </summary>
    [Fact]
    public async Task SetDependencies_VoidActionWithDependency_GeneratesCorrectly()
    {
        var source = CommonUsings + """

            namespace TestApp.Notifications;

            public interface IEmailService { }

            [DomainAction]
            public partial class SendEmail : VoidDomainAction
            {
                private IEmailService _emailService;

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    #region Invoker Snapshots

    /// <summary>
    ///     Verifies the generated Invoker for a simple DomainAction with return type.
    /// </summary>
    [Fact]
    public async Task Invoker_DomainActionWithResult_GeneratesCorrectly()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class GetOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("order-1"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated Invoker for a VoidDomainAction.
    /// </summary>
    [Fact]
    public async Task Invoker_VoidDomainAction_GeneratesVoidInvoker()
    {
        var source = CommonUsings + """

            namespace TestApp.Notifications;

            public interface IEmailService { }

            [DomainAction]
            public partial class SendNotification : VoidDomainAction
            {
                private IEmailService _emailService;

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated Invoker for an action with typed errors.
    /// </summary>
    [Fact]
    public async Task Invoker_WithTypedErrors_GeneratesCorrectGenericParams()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public class ValidationError : IError
            {
                public string Code => "VALIDATION";
                public int StatusCode => 422;
            }

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string, ValidationError>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated Invoker for an action with no dependencies (empty InjectDependencies).
    /// </summary>
    [Fact]
    public async Task Invoker_NoDependencies_GeneratesEmptyInjectDependencies()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public partial class NoDepAction : DomainAction<int>
            {
                public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<int, IError>.Success(42));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated Invoker includes a per-action DI extension method.
    /// </summary>
    [Fact]
    public async Task Invoker_GeneratesPerActionDIExtension()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class CreateOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("order-1"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    #region Registration Snapshots

    /// <summary>
    ///     Verifies the generated aggregate registration for a single action.
    /// </summary>
    [Fact]
    public async Task Registration_SingleAction_GeneratesCorrectly()
    {
        var source = CommonUsings + """

            namespace TestApp.Actions;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated aggregate registration for multiple actions.
    /// </summary>
    [Fact]
    public async Task Registration_MultipleActions_AggregatesAll()
    {
        var source = CommonUsings + """

            namespace TestApp.Actions;

            public interface IOrderRepository { }
            public interface IEmailService { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }

            [DomainAction]
            public partial class CancelOrder : VoidDomainAction
            {
                private IEmailService _emailService;

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }

            [DomainAction]
            public partial class GetOrderStatus : DomainAction<int>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<int, IError>.Success(1));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    #region Metadata Snapshots

    /// <summary>
    ///     Verifies the generated metadata when Composition is referenced.
    /// </summary>
    [Fact]
    public async Task Metadata_WithComposition_GeneratesMetadataAttribute()
    {
        var source = CommonUsings + """

            namespace TestApp.Actions;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGeneratorWithComposition(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies metadata contains void action info.
    /// </summary>
    [Fact]
    public async Task Metadata_VoidAction_ContainsIsVoidFlag()
    {
        var source = CommonUsings + """

            namespace TestApp.Actions;

            public interface IEmailService { }

            [DomainAction]
            public partial class SendEmail : VoidDomainAction
            {
                private IEmailService _emailService;

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGeneratorWithComposition(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies metadata for multiple actions counts correctly.
    /// </summary>
    [Fact]
    public async Task Metadata_MultipleActions_CountsAndListsAll()
    {
        var source = CommonUsings + """

            namespace TestApp.Actions;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }

            [DomainAction]
            public partial class CancelOrder : VoidDomainAction
            {
                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGeneratorWithComposition(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    #region Edge Case Snapshots

    /// <summary>
    ///     Verifies generation for an action in a deeply nested namespace.
    /// </summary>
    [Fact]
    public async Task EdgeCase_NestedNamespace_GeneratesCorrectlyWithFullNamespace()
    {
        var source = CommonUsings + """

            namespace MyCompany.Sales.Orders.Commands;

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies generation for an internal action (not included in boundary).
    /// </summary>
    [Fact]
    public async Task EdgeCase_InternalAction_GeneratesWithInternalFlag()
    {
        var source = CommonUsings + """

            namespace TestApp.Actions;

            public interface IOrderRepository { }

            [DomainAction(Internal = true)]
            public partial class RecalculateTotal : DomainAction<decimal>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<decimal, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<decimal, IError>.Success(99.99m));
            }
            """;

        var result = RunGeneratorWithComposition(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies generation for a system action.
    /// </summary>
    [Fact]
    public async Task EdgeCase_SystemAction_GeneratesWithSystemFlag()
    {
        var source = CommonUsings + """

            namespace TestApp.Actions;

            [DomainAction(System = true)]
            public partial class HealthCheck : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("OK"));
            }
            """;

        var result = RunGeneratorWithComposition(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies generation for actions in different namespaces within the same assembly.
    /// </summary>
    [Fact]
    public async Task EdgeCase_DifferentNamespaces_GeneratesRegistrationWithCommonPrefix()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders
            {
                public interface IOrderRepository { }

                [DomainAction]
                public partial class PlaceOrder : DomainAction<string>
                {
                    private IOrderRepository _orderRepository;

                    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<string, IError>.Success("done"));
                }
            }

            namespace TestApp.Payments
            {
                public interface IPaymentService { }

                [DomainAction]
                public partial class ProcessPayment : DomainAction<bool>
                {
                    private IPaymentService _paymentService;

                    public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(Result<bool, IError>.Success(true));
                }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     A non-partial action is skipped. PRAG0400 is the companion analyzer's, on the declaration.
    /// </summary>
    [Fact]
    public async Task Diagnostic_NonPartialClass_IsSkippedWithoutAGeneratorDiagnostic()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public class NotPartialAction : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0400").Should().BeFalse();

        // Should still produce output for valid parts (registration may be empty)
        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the no-base-type diagnostic (PRAG0401).
    /// </summary>
    [Fact]
    public async Task Diagnostic_NoBaseType_EmitsPRAG0401()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [DomainAction]
            public partial class BadAction
            {
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0401").Should().BeTrue();

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies generation for a DomainAction with multiple typed error parameters.
    /// </summary>
    [Fact]
    public async Task EdgeCase_MultipleErrorTypes_GeneratesCorrectly()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public class ValidationError : IError
            {
                public string Code => "VALIDATION";
                public int StatusCode => 422;
            }

            public class ConflictError : IError
            {
                public string Code => "CONFLICT";
                public int StatusCode => 409;
            }

            public interface IOrderRepository { }

            [DomainAction]
            public partial class PlaceOrder : DomainAction<string, ValidationError, ConflictError>
            {
                private IOrderRepository _orderRepository;

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion
}
