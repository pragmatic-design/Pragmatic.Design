using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Lifecycle;

/// <summary>
///     PRAG2753 — <c>[Raises&lt;T&gt;]</c> on an <b>entity's method</b> generates nothing, and without the
///     diagnostic it would compile in silence: the declaration read as wired because the class-level form on
///     that same entity is. The generator refuses it and names the two shapes that do work.
///     The controls are what make "refused" mean something: the class-level form stays accepted, and a
///     method of a type that is not an entity keeps declaring a raise for the event graph — that is
///     where PRAG0816 reads its origin from (<c>RecallDrugAction.Execute()</c>).
/// </summary>
public class RaisesOnAnEntityMethodTests
{
    private const string Shims = """
        namespace Pragmatic.Events
        {
            public enum EntityLifecycle { Created, Updated, Deleted }
            public interface IRaisesLifecycleEvents { void RaiseLifecycleEvents(EntityLifecycle lifecycle); }
            public interface IDomainEvent { System.DateTimeOffset OccurredAt { get; } }
            public abstract class DomainEventSource { protected void RaiseEvent(IDomainEvent @event) { } }
        }
        namespace Pragmatic.Authoring
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = true)]
            public sealed class RaisesAttribute<TEvent> : System.Attribute where TEvent : notnull
            {
                public RaisesAttribute(Pragmatic.Events.EntityLifecycle on = Pragmatic.Events.EntityLifecycle.Created) => On = on;
                public Pragmatic.Events.EntityLifecycle On { get; }
            }
        }
        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EntityAttribute : System.Attribute { }
        }
        """;

    private const string EventDeclaration = """

        namespace MyApp
        {
            public sealed record DrugRecalled(System.Guid DrugId, System.DateTimeOffset OccurredAt)
                : Pragmatic.Events.IDomainEvent;
        }
        """;

    [Fact]
    public void Raises_OnAnEntityMethod_ReportsPRAG2753()
    {
        var source = Shims + EventDeclaration + """

            namespace MyApp
            {
                public partial class Drug : Pragmatic.Events.DomainEventSource
                {
                    public System.Guid Id { get; set; }

                    [Pragmatic.Authoring.Raises<DrugRecalled>]
                    public void Recall() { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2753").Should().BeTrue();
    }

    [Fact]
    public void Raises_OnAnEntityMethod_NamesTheTwoShapesThatWork()
    {
        var source = Shims + EventDeclaration + """

            namespace MyApp
            {
                public partial class Drug : Pragmatic.Events.DomainEventSource
                {
                    public System.Guid Id { get; set; }

                    [Pragmatic.Authoring.Raises<DrugRecalled>]
                    public void Recall() { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var message = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG2753")
            .Select(d => d.GetMessage())
            .Single();

        // A diagnostic that only says "no" leaves the author where the silence did.
        message.Should().Contain("Drug.Recall()");
        message.Should().Contain("RaisesEvent");
        message.Should().Contain("RaiseEvent(");
    }

    [Fact]
    public void Raises_OnAnEntityMethod_IsReportedEvenWithoutDomainEventSource()
    {
        // An [Entity] that forgot the base class: PRAG2750 covers the class-level form, nothing
        // covered this one, and the author gets the same silence.
        var source = Shims + EventDeclaration + """

            namespace MyApp
            {
                [Pragmatic.Persistence.Entity.Entity]
                public partial class Drug
                {
                    public System.Guid Id { get; set; }

                    [Pragmatic.Authoring.Raises<DrugRecalled>]
                    public void Recall() { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2753").Should().BeTrue();
    }

    [Fact]
    public void Raises_OnTheEntityClass_IsNotReported()
    {
        // The control: the shape the framework wires must stay accepted. "Refused" is otherwise
        // satisfied by refusing everything.
        var source = Shims + EventDeclaration + """

            namespace MyApp
            {
                [Pragmatic.Authoring.Raises<DrugRecalled>]
                public partial class Drug : Pragmatic.Events.DomainEventSource
                {
                    public System.Guid Id { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2753").Should().BeFalse();
        GeneratorTestHelper.GetGeneratedSource(result, "Drug.LifecycleEvents.g.cs")
            .Should().Contain("RaiseEvent(new global::MyApp.DrugRecalled(Id, global::System.DateTimeOffset.UtcNow));");
    }

    [Fact]
    public void Raises_OnAMethodOfSomethingThatIsNotAnEntity_IsNotReported()
    {
        // The second control: on a non-entity the member-level declaration is what the host's event
        // graph reads to attribute a raise to its origin. Refusing it there would delete a working
        // feature to fix an unrelated silence.
        var source = Shims + EventDeclaration + """

            namespace MyApp
            {
                public class RecallDrugAction
                {
                    [Pragmatic.Authoring.Raises<DrugRecalled>]
                    public void Execute() { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2753").Should().BeFalse();
    }
}
