using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     <c>[Service&lt;IActionFilter&lt;TAction&gt;&gt;]</c> where <c>TAction</c> is an action another
///     generator writes.
/// </summary>
/// <remarks>
///     A guard on the attachments upload action is registered this way, and an emitted DI registration
///     that does not compile pushes the author back to a hand-written <c>AddScoped</c>. This test uses
///     the real shape: the action type genuinely does not exist when the attribute is read, because
///     the trait writes it in the same pass. Either the registration is correct, or this test names
///     what is wrong with it.
/// </remarks>
public class ServiceOnGeneratedActionTests
{
    private const string Source = """
        using Pragmatic.Attachments;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Actions.Pipeline;

        namespace Billing.Invoices
        {
            public sealed class BillingBoundary { }

            [Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<BillingBoundary>]
            [Resource("invoices")]
            [HasAttachments]
            public partial class Invoice : IEntity
            {
                public System.Guid Id { get; set; }
                public System.Guid PersistenceId { get => Id; set => Id = value; }
            }

            // UploadInvoiceAttachmentAction is written by the trait: it does not exist yet here.
            [Service<IActionFilter<UploadInvoiceAttachmentAction>>]
            public sealed class UploadGuard { }

            [PragmaticDbContext("Billing")]
            public partial class BillingDbContext { }
        }
        """;

    /// <summary>
    ///     The generator says so instead of emitting a name that binds by luck.
    /// </summary>
    [Fact]
    public void UnresolvableTypeArgument_IsReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Source);

        diagnostics.Should().Contain(d => d.Id == "PRAG1614",
            "the action does not exist when the attribute is read, so the registration cannot qualify it");
    }

    /// <summary>
    ///     The shape the diagnostic is about, pinned: the outer interface is fully qualified and the
    ///     argument is not. A later fix that qualifies it has to change this test too.
    /// </summary>
    [Fact]
    public void UnresolvableTypeArgument_IsEmittedUnqualified()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source);

        var registration = string.Join("\n", sources
            .Where(s => s.Key.Contains("ServiceRegistration"))
            .Select(s => s.Value));

        registration.Should().Contain("IActionFilter<UploadInvoiceAttachmentAction>",
            "this is the defect PRAG1614 warns about, not the intended output");
    }

    /// <summary>A type argument that does resolve must not be reported.</summary>
    [Fact]
    public void ResolvableTypeArgument_IsNotReported()
    {
        var resolvable = Source.Replace(
            "[Service<IActionFilter<UploadInvoiceAttachmentAction>>]",
            "[Service<IActionFilter<Invoice>>]");

        var (_, diagnostics) = TraitCompilationHarness.Generate(resolvable);

        diagnostics.Should().NotContain(d => d.Id == "PRAG1614");
    }
}
