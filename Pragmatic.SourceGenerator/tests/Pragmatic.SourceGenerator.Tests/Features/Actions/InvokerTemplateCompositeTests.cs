using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

public class InvokerTemplateCompositeTests
{
    [Fact]
    public void CompositeAction_GeneratesBatchContextWrapper()
    {
        var model = BuildModel("Sales", "CreateInvoiceWithFees", isComposite: true);
        var source = Render(model);

        source.Should().Contain("BatchContext");
        source.Should().Contain("using var batch = new global::Pragmatic.Persistence.Lifecycle.BatchContext()");
        source.Should().Contain("ExecuteActionAsync");
    }

    [Fact]
    public void NonCompositeAction_DoesNotGenerateBatchContext()
    {
        var model = BuildModel("Sales", "PlaceOrder", isComposite: false);
        var source = Render(model);

        source.Should().NotContain("BatchContext");
    }

    [Fact]
    public void CompositeAction_DelegatesExecuteInsideBatch()
    {
        var model = BuildModel("Sales", "CreateInvoiceWithFees", isComposite: true);
        var source = Render(model);

        source.Should().Contain("return await action.Execute(ct).ConfigureAwait(false)");
    }

    [Fact]
    public void CompositeAction_SingleSaveChangesOutsideBatch()
    {
        var model = BuildModel("Sales", "CreateInvoiceWithFees", isComposite: true,
            belongsTo: "global::Sales.SalesBoundary");
        var source = Render(model);

        // SaveChangesAsync is generated (via boundary) but NOT inside ExecuteActionAsync
        source.Should().Contain("SaveChangesAsync");
        source.Should().Contain("_unitOfWork.SaveChangesAsync");

        // BatchContext and SaveChanges are in separate methods
        var executeStart = source.IndexOf("ExecuteActionAsync");
        var executeSection = source.Substring(executeStart, Math.Min(400, source.Length - executeStart));
        executeSection.Should().Contain("BatchContext");
        executeSection.Should().NotContain("_unitOfWork");
    }

    [Fact]
    public void CompositeAction_XmlSummaryMentionsAtomic()
    {
        var model = BuildModel("Sales", "CreateInvoiceWithFees", isComposite: true);
        var source = Render(model);

        source.Should().Contain("Wraps action execution in a BatchContext for atomic commit");
    }

    private static string Render(ActionModel model)
    {
        var template = new InvokerTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static ActionModel BuildModel(string ns, string name,
        bool isComposite = false, string? belongsTo = null)
    {
        return new ActionModel
        {
            Namespace = ns,
            TypeName = name,
            FullTypeName = $"global::{ns}.{name}",
            Accessibility = "public",
            IsVoid = false,
            ReturnTypeName = "global::System.Guid",
            IsComposite = isComposite,
            BelongsToTypeName = belongsTo,
            Dependencies = ImmutableArray<DependencyModel>.Empty
        };
    }
}
