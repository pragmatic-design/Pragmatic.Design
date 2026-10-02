using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     G2 (end-to-end): two types with the same simple name in different namespaces must not produce the
///     same generated hint name. A duplicate hint makes <c>AddSource</c> throw and kills ALL code
///     generation, so this exercises a real generator run rather than just <c>VirtualFolderHints.ForType</c>.
/// </summary>
public class HintNameCollisionE2ETests
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

    [Fact]
    public void SameSimpleName_DifferentNamespaces_DoNotCollide()
    {
        var source = Shims + """

            namespace Sales
            {
                public sealed record InvoiceRegistered(System.Guid Id, System.DateTimeOffset OccurredAt) : Pragmatic.Events.IDomainEvent;

                [Pragmatic.Authoring.Raises<InvoiceRegistered>]
                public partial class Invoice : Pragmatic.Events.DomainEventSource { public System.Guid Id { get; set; } }
            }

            namespace Archive
            {
                public sealed record InvoiceArchived(System.Guid Id, System.DateTimeOffset OccurredAt) : Pragmatic.Events.IDomainEvent;

                [Pragmatic.Authoring.Raises<InvoiceArchived>]
                public partial class Invoice : Pragmatic.Events.DomainEventSource { public System.Guid Id { get; set; } }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        // No duplicate-hint crash, and BOTH entities generated their lifecycle partial under distinct hints.
        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();

        var hints = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys;
        hints.Should().Contain(k => k.Contains("Sales.Invoice.LifecycleEvents"));
        hints.Should().Contain(k => k.Contains("Archive.Invoice.LifecycleEvents"));
    }

    private const string ValueObjectShims = """
        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class ValueObjectAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void ValueObject_SameSimpleName_DifferentNamespaces_DoNotCollide()
    {
        var source = ValueObjectShims + """

            namespace Sales
            {
                [Pragmatic.Persistence.Entity.ValueObject]
                public partial record Email(string Value);
            }

            namespace Billing
            {
                [Pragmatic.Persistence.Entity.ValueObject]
                public partial record Email(string Value);
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();

        var hints = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys;
        hints.Should().Contain(k => k.Contains("Sales.Email.ValueObject"));
        hints.Should().Contain(k => k.Contains("Billing.Email.ValueObject"));
    }

    private static NoteTraitModel NoteModel(string parentNamespace) => new()
    {
        ParentTypeName = "Order",
        ParentNamespace = parentNamespace,
        ParentFullTypeName = $"{parentNamespace}.Order",
        IdType = "System.Guid",
        BoundaryName = "Sales",
        ResourceSegment = "orders",
        ResourceParamName = "orderId",
    };

    /// <summary>
    ///     Traits generate per-parent artifacts. Two parents with the same simple name in different
    ///     namespaces (<c>Sales.Order</c> / <c>Billing.Order</c>) must produce distinct hints for every
    ///     artifact, otherwise the second <c>AddSource</c> aborts the whole generator run.
    /// </summary>
    /// <remarks>
    ///     Asserted on the templates rather than on a full generator run: the trait pipeline still aborts
    ///     end-to-end because <c>VirtualFolderHints.ForEntityConfig</c> takes no namespace, so
    ///     <c>EntityConfig.OrderNote.g.cs</c> remains ambiguous. That helper's signature is shared with the
    ///     host-side EntityConfig convention and is tracked separately.
    /// </remarks>
    [Fact]
    public void Traits_SameSimpleName_DifferentNamespaces_ProduceDistinctHints()
    {
        var sales = NoteModel("Sales");
        var billing = NoteModel("Billing");

        static string[] Hints(NoteTraitModel m) =>
        [
            new NoteEntityTemplate(m).RenderOutput().HintName,
            new ParentNoteNavigationTemplate(m).RenderOutput().HintName,
            new NoteDtoTemplate(m).RenderOutput().HintName,
            new NoteListQueryTemplate(m).RenderOutput().HintName,
            new NotePermissionsTemplate(m).RenderOutput().HintName,
            new NoteActionsTemplate(m, NoteActionKind.Add).RenderOutput().HintName,
            new NoteActionsTemplate(m, NoteActionKind.Update).RenderOutput().HintName,
            new NoteActionsTemplate(m, NoteActionKind.Delete).RenderOutput().HintName,
            new NoteActionsTemplate(m, NoteActionKind.GetById).RenderOutput().HintName,
        ];

        var salesHints = Hints(sales);
        var billingHints = Hints(billing);

        salesHints.Should().OnlyContain(h => h.StartsWith("Sales."));
        billingHints.Should().OnlyContain(h => h.StartsWith("Billing."));
        salesHints.Should().NotIntersectWith(billingHints);

        salesHints.Should().Contain("Sales.OrderNote.Entity.g.cs");
        billingHints.Should().Contain("Billing.OrderNote.Entity.g.cs");
    }
}
