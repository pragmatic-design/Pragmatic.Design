using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     PRAG0447 — a composite no boundary claims.
/// </summary>
/// <remarks>
///     The generated composite invoker asks for a unit of work, which is registered per boundary. When
///     the namespace matches no boundary and the type declares no <c>[BelongsTo&lt;TBoundary&gt;]</c>,
///     the invoker is emitted asking for one nobody registers and the application fails to start on a
///     container validation error naming a generated type. The diagnostic is the one thing standing
///     between that and a build that looks clean, and these tests assert it is emitted.
/// </remarks>
public class CompositeWithoutBoundaryTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        """;

    /// <param name="boundary">The boundary declaration, or nothing at all.</param>
    private static string Source(string boundary) => CommonUsings + $$"""

        namespace TestApp.Sales
        {
            {{boundary}}

            public partial class Reservation : IEntity
            {
                public Guid PersistenceId { get; set; }
            }

            [Mutation(Mode = MutationMode.Create)]
            public partial class CreateReservationMutation : Mutation<Reservation>
            {
                public required string RoomNumber { get; init; }
            }

            [DomainAction]
            [CompositeAction]
            public partial class PlaceOrderComposite : VoidDomainAction
            {
                public required CreateReservationMutation Reservation { get; init; }
            }
        }
        """;

    [Fact]
    public void ACompositeNoBoundaryClaims_ReportsPrag0447()
    {
        var result = RunGeneratorWithEntities(Source(boundary: ""));

        HasDiagnostic(result, "PRAG0447").Should().BeTrue(
            "the invoker needs a unit of work, which is registered per boundary, and no boundary claims this composite");
    }

    /// <summary>The control: a boundary over the composite's namespace, and the diagnostic is silent.</summary>
    [Fact]
    public void ACompositeInsideItsBoundarysNamespace_IsSilent()
    {
        var result = RunGeneratorWithEntities(Source("[Boundary] public partial class SalesBoundary;"));

        HasDiagnostic(result, "PRAG0447").Should().BeFalse();
    }
}
