using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Lifecycle;

/// <summary>
/// Tests for the lifecycle-events generator: <c>[Raises&lt;TEvent&gt;(on: ...)]</c> on an entity generates
/// an <c>IRaisesLifecycleEvents</c> partial that raises the event at its transition, filling the event
/// constructor from entity members by name. Marker/runtime types are defined inline in their real
/// namespaces so <c>ForAttributeWithMetadataName</c> matches and the generated code compiles.
/// </summary>
public class LifecycleEventsGeneratorTests
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
        """;

    private const string Drug = """

        namespace MyApp
        {
            public sealed record DrugRegistered(System.Guid DrugId, string Code, System.DateTimeOffset OccurredAt)
                : Pragmatic.Events.IDomainEvent;

            [Pragmatic.Authoring.Raises<DrugRegistered>]
            public partial class Drug : Pragmatic.Events.DomainEventSource
            {
                public System.Guid Id { get; set; }
                public string Code { get; set; } = "";
            }
        }
        """;

    [Fact]
    public void Raises_OnCreate_GeneratesLifecyclePartialImplementingInterface()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Shims + Drug, []);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Drug.LifecycleEvents.g.cs");
        generated.Should().Contain("partial class Drug : global::Pragmatic.Events.IRaisesLifecycleEvents");
        generated.Should().Contain("void global::Pragmatic.Events.IRaisesLifecycleEvents.RaiseLifecycleEvents(global::Pragmatic.Events.EntityLifecycle lifecycle)");
        generated.Should().Contain("if (lifecycle == global::Pragmatic.Events.EntityLifecycle.Created)");
    }

    [Fact]
    public void Raises_MatchesEventConstructorToEntityMembersByName()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Shims + Drug, []);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Drug.LifecycleEvents.g.cs");
        // DrugId -> Id ({Entity}Id convention), Code -> Code (member), OccurredAt -> clock.
        generated.Should().Contain("RaiseEvent(new global::MyApp.DrugRegistered(Id, Code, global::System.DateTimeOffset.UtcNow));");
    }

    [Fact]
    public void Raises_OnDelete_UsesDeletedLifecycle()
    {
        var source = Shims + """

            namespace MyApp
            {
                public sealed record DrugDeleted(System.Guid DrugId, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IDomainEvent;

                [Pragmatic.Authoring.Raises<DrugDeleted>(Pragmatic.Events.EntityLifecycle.Deleted)]
                public partial class Drug : Pragmatic.Events.DomainEventSource
                {
                    public System.Guid Id { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Drug.LifecycleEvents.g.cs");
        generated.Should().Contain("if (lifecycle == global::Pragmatic.Events.EntityLifecycle.Deleted)");
        generated.Should().Contain("RaiseEvent(new global::MyApp.DrugDeleted(Id, global::System.DateTimeOffset.UtcNow));");
    }

    [Fact]
    public void Raises_OnNonDomainEventSource_ReportsPRAG2750_AndGeneratesNothing()
    {
        var source = Shims + """

            namespace MyApp
            {
                public sealed record DrugRegistered(System.Guid DrugId, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IDomainEvent;

                [Pragmatic.Authoring.Raises<DrugRegistered>]
                public partial class Drug   // does NOT derive from DomainEventSource
                {
                    public System.Guid Id { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2750").Should().BeTrue();
    }

    [Fact]
    public void Raises_OnHomonymousApplicationBaseClass_StillReportsPRAG2750()
    {
        // The base-class probe must be an identity check, not a name match. An application
        // class merely CALLED DomainEventSource has no RaiseEvent, so accepting it suppresses the
        // diagnostic and turns the promised error into a compile error inside the generated file.
        var source = Shims + """

            namespace MyApp.Domain
            {
                public abstract class DomainEventSource { }   // homonym, unrelated to Pragmatic.Events
            }

            namespace MyApp
            {
                public sealed record DrugRegistered(System.Guid DrugId, System.DateTimeOffset OccurredAt)
                    : Pragmatic.Events.IDomainEvent;

                [Pragmatic.Authoring.Raises<DrugRegistered>]
                public partial class Drug : MyApp.Domain.DomainEventSource
                {
                    public System.Guid Id { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2750").Should().BeTrue();
        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Should()
            .NotContainKey("Drug.LifecycleEvents.g.cs", "nothing may be generated for a non-event-source entity");
    }

    [Fact]
    public void Raises_GeneratedCode_Compiles()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Shims + Drug, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();
    }
}
