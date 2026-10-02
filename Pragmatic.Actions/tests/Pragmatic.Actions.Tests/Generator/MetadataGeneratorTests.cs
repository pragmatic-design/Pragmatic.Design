using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Tests for [PragmaticMetadata] generation when Composition is referenced.
/// </summary>
public class MetadataGeneratorTests : ActionsGeneratorTestBase
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
    public void WithComposition_GeneratesMetadataAttribute()
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

        var generated = GetGeneratedSource(result, "_Metadata.Actions");
        generated.Should().NotBeNull();
        generated.Should().Contain("PragmaticMetadata");
        generated.Should().Contain("MetadataCategory.Actions");
        generated.Should().Contain("Pragmatic.Actions.SourceGenerator");
    }

    [Fact]
    public void WithoutExplicitComposition_StillGeneratesMetadata()
    {
        // PragmaticMetadataAttribute is now in Pragmatic.Abstractions (always available),
        // so metadata is generated even without explicit Composition reference.
        var source = CommonUsings + """

            namespace TestApp.Actions;

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

        var generated = GetGeneratedSource(result, "_Metadata.");
        generated.Should().NotBeNull("metadata should be generated since PragmaticMetadataAttribute is in Abstractions");
    }

    [Fact]
    public void Metadata_ContainsActionDetails()
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

        var generated = GetGeneratedSource(result, "_Metadata.Actions");
        generated.Should().NotBeNull();
        generated.Should().Contain("actionsCount");
        generated.Should().Contain("PlaceOrder");
        generated.Should().Contain("registrationMethod");
    }

    [Fact]
    public void Metadata_ContainsVoidActionInfo()
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

        var generated = GetGeneratedSource(result, "_Metadata.Actions");
        generated.Should().NotBeNull();
        generated.Should().Contain("SendEmail");
        generated.Should().Contain("isVoid");
    }

    [Fact]
    public void Metadata_MultipleActions_CountsCorrectly()
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

        var generated = GetGeneratedSource(result, "_Metadata.Actions");
        generated.Should().NotBeNull();
        generated.Should().Contain("PlaceOrder");
        generated.Should().Contain("CancelOrder");
    }
}
