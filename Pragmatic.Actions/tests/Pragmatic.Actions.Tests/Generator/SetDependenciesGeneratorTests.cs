using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for SetDependencies generation.
/// </summary>
public class SetDependenciesGeneratorTests : ActionsGeneratorTestBase
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
    public void SimpleAction_WithOneDependency_GeneratesSetDependencies()
    {
        var source = CommonUsings + """

            namespace TestApp;

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

        var generated = GetGeneratedSource(result, "SetDependencies");
        generated.Should().NotBeNull();
        generated.Should().Contain("SetDependencies");
        generated.Should().Contain("IOrderRepository");
        generated.Should().Contain("_orderRepository = orderRepository;");
    }

    [Fact]
    public void SimpleAction_WithMultipleDependencies_GeneratesAllParameters()
    {
        var source = CommonUsings + """
            using Microsoft.Extensions.Logging;

            namespace TestApp;

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

        var generated = GetGeneratedSource(result, "SetDependencies");
        generated.Should().NotBeNull();
        generated.Should().Contain("IOrderRepository");
        generated.Should().Contain("ILogger");
        generated.Should().Contain("IOrderService");
    }

    [Fact]
    public void Action_WithNoDependencies_DoesNotGenerateSetDependencies()
    {
        var source = CommonUsings + """

            namespace TestApp;

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

        var generated = GetGeneratedSource(result, "SetDependencies");
        generated.Should().BeNull("no SetDependencies should be generated when there are no dependencies");
    }

    [Fact]
    public void VoidAction_WithDependency_GeneratesSetDependencies()
    {
        var source = CommonUsings + """

            namespace TestApp;

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

        var generated = GetGeneratedSource(result, "SetDependencies");
        generated.Should().NotBeNull();
        generated.Should().Contain("IEmailService");
        generated.Should().Contain("_emailService = emailService;");
    }

    [Fact]
    public void NonPartialClass_IsSkippedWithoutAGeneratorDiagnostic()
    {
        var source = CommonUsings + """

            namespace TestApp;

            [DomainAction]
            public class NotPartialAction : DomainAction<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("done"));
            }
            """;

        var result = RunGenerator(source);

        // PRAG0400 is the companion analyzer's, on the declaration.
        HasDiagnostic(result, "PRAG0400").Should().BeFalse();
        GetGeneratedSource(result, "NotPartialAction").Should().BeNull("a non-partial type cannot take a generated part");
    }

    [Fact]
    public void ClassWithoutBaseType_EmitsPRAG0401Diagnostic()
    {
        var source = CommonUsings + """

            namespace TestApp;

            [DomainAction]
            public partial class BadAction
            {
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0401").Should().BeTrue();
    }
}
