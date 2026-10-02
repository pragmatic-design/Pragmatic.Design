using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

public class CompositeActionInvokerTemplateTests
{
    [Fact]
    public void RenderOutput_GeneratesCompositeInvokerClass()
    {
        var model = BuildModel("Sales", "BookAndInvoice");
        var source = Render(model);

        source.Should().Contain("class CompositeInvoker");
        source.Should().Contain("sealed class");
    }

    [Fact]
    public void RenderOutput_InjectsUnitOfWork()
    {
        var model = BuildModel("Sales", "BookAndInvoice");
        var source = Render(model);

        source.Should().Contain("IUnitOfWork");
        source.Should().Contain("_unitOfWork");
    }

    [Fact]
    public void RenderOutput_InjectsMutationInvokers()
    {
        var model = BuildModel("Sales", "BookAndInvoice");
        var source = Render(model);

        source.Should().Contain("_createReservationInvoker");
        source.Should().Contain("_createInvoiceInvoker");
    }

    /// <summary>
    ///     Every step is invoked the ordinary way, with no special no-save entry point: the composite
    ///     claims the unit of work first, so a plain invocation already stages — which is also what makes
    ///     action steps possible, since actions have no such entry point.
    /// </summary>
    [Fact]
    public void ExecuteAsync_InvokesEachStep()
    {
        var model = BuildModel("Sales", "BookAndInvoice");
        var source = Render(model);

        source.Should().Contain("InvokeAsync(action.CreateReservation");
        source.Should().Contain("InvokeAsync(action.CreateInvoice");
    }

    [Fact]
    public void ExecuteAsync_SingleSaveChanges()
    {
        var model = BuildModel("Sales", "BookAndInvoice");
        var source = Render(model);

        // Should have exactly one SaveChangesAsync call (at the end)
        var count = CountOccurrences(source, "SaveChangesAsync");
        count.Should().Be(1);
    }

    [Fact]
    public void ExecuteAsync_WrapsStepsInSingleBatchAndFlushesDeferredEvents()
    {
        var model = BuildModel("Sales", "BookAndInvoice");
        var source = Render(model);

        // One batch for all the steps; deferred events dispatched once after the single commit,
        // otherwise the scope ends and drops them. The batch is the one CommitScope opens for this
        // unit of work — not an ambient one covering every boundary, which would include boundaries the
        // composite has no way to save.
        source.Should().Contain("CommitScope.Claim");
        source.Should().Contain("global::Pragmatic.Events.IDomainEventDispatcher");
        source.Should().Contain("__batch.DeferredEvents");
        source.Should().Contain("global::Pragmatic.Caching.ICacheInvalidator");
    }

    [Fact]
    public void ExecuteAsync_ChecksFailureAfterEachStep()
    {
        var model = BuildModel("Sales", "BookAndInvoice");
        var source = Render(model);

        source.Should().Contain("createReservationResult.IsFailure");
        source.Should().Contain("createInvoiceResult.IsFailure");
    }

    [Fact]
    public void RenderOutput_DoesNotGenerateDiExtension()
    {
        var model = BuildModel("Sales", "BookAndInvoice");
        var source = Render(model);

        source.Should().NotContain("InvokerExtensions");
        source.Should().NotContain("AddBookAndInvoiceCompositeInvoker");
        source.Should().NotContain("AddScoped<BookAndInvoiceCompositeInvoker>");
    }

    [Fact]
    public void RenderOutput_WithBoundary_UsesKeyedService()
    {
        var model = BuildModel("Sales", "BookAndInvoice", belongsTo: "global::Sales.SalesBoundary");
        var source = Render(model);

        source.Should().Contain("FromKeyedServices(typeof(global::Sales.SalesBoundary))");
    }

    [Fact]
    public void Validate_NoSteps_ReturnsEmpty()
    {
        var model = new CompositeActionModel
        {
            Namespace = "Sales",
            TypeName = "Empty",
            FullTypeName = "global::Sales.Empty",
            Accessibility = "public",
            Steps = ImmutableArray<CompositeStepModel>.Empty
        };

        var template = new CompositeActionInvokerTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void RenderOutput_VoidAction_ReturnsVoidResult()
    {
        var model = BuildModel("Sales", "BookAndInvoice", isVoid: true);
        var source = Render(model);

        source.Should().Contain("VoidResult<");
        source.Should().Contain("VoidResult<global::Pragmatic.Result.IError>.Success()");
    }

    private static string Render(CompositeActionModel model)
    {
        var template = new CompositeActionInvokerTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static CompositeActionModel BuildModel(string ns, string name,
        string? belongsTo = null, bool isVoid = false)
    {
        return new CompositeActionModel
        {
            Namespace = ns,
            TypeName = name,
            FullTypeName = $"global::{ns}.{name}",
            Accessibility = "public",
            IsVoid = isVoid,
            ReturnTypeName = isVoid ? null : "object",
            BelongsToTypeName = belongsTo,
            Steps = ImmutableArray.Create(
                new CompositeStepModel
                {
                    PropertyName = "CreateReservation",
                    Kind = CompositeStepKind.Mutation,
                    StepFullTypeName = $"global::{ns}.CreateReservationMutation",
                    StepTypeName = "CreateReservationMutation",
                    ResultFullTypeName = $"global::{ns}.Reservation",
                    InvokerFullTypeName = $"global::{ns}.CreateReservationMutation.Invoker"
                },
                new CompositeStepModel
                {
                    PropertyName = "CreateInvoice",
                    Kind = CompositeStepKind.Mutation,
                    StepFullTypeName = $"global::{ns}.CreateInvoiceMutation",
                    StepTypeName = "CreateInvoiceMutation",
                    ResultFullTypeName = $"global::{ns}.Invoice",
                    InvokerFullTypeName = $"global::{ns}.CreateInvoiceMutation.Invoker"
                })
        };
    }

    private static int CountOccurrences(string source, string search)
    {
        var count = 0;
        var idx = 0;
        while ((idx = source.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }

        return count;
    }
}
