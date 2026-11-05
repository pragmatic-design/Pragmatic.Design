using Pragmatic.Messaging.Engines.Documents;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     An <c>[EnableSagaPersistence]</c> boundary on the documents engine: its generated <c>{Boundary}DocSchema</c> holds the saga store's two
///     collections, as Pragmatic.Messaging.Engines declares them, beside the boundary's own, so <c>EnsureAsync</c> creates them with the rest.
/// </summary>
public sealed class SagaCollectionsInTheDocSchemaTests
{
    [Fact]
    public void The_boundarys_schema_holds_the_saga_collections()
    {
        Assert.Same(SagaInstanceDocument.Schema, Assert.Single(MailroomBoundaryDocSchema.Collections, c => c.Collection == "__SagaInstances"));
        Assert.Same(SagaStepDocument.Schema, Assert.Single(MailroomBoundaryDocSchema.Collections, c => c.Collection == "__SagaSteps"));
        Assert.Contains(MailroomBoundaryDocSchema.Collections, c => c.Collection == "Bulletins"); // the control: the boundary's own
    }
}
