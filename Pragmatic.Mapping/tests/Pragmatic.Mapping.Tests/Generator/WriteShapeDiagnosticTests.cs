using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Three diagnostics about the shape of a mapping that were emitted and never measured:
///     <c>PRAG0313</c> (a cycle in the graph), <c>PRAG0335</c> (<c>[LinkIds]</c> outside the tracked
///     form) and <c>PRAG0336</c> (a write aimed at a property the entity computes).
/// </summary>
/// <remarks>
///     Each with its control. A diagnostic asserted only positively is satisfied by a generator that
///     reports it on every input.
/// </remarks>
public class WriteShapeDiagnosticTests : MappingGeneratorTestBase
{
    // ── PRAG0313: a cycle, closed by instance tracking ────────────────────────

    /// <param name="leader">The member on <c>Team</c> that closes the cycle, or nothing.</param>
    private static string Cycle(string leader) => $$"""
        using Pragmatic.Mapping.Attributes;

        namespace TestApp
        {
            public class User
            {
                public int Id { get; set; }
                public Team? Team { get; set; }
            }

            public class Team
            {
                public int Id { get; set; }
                public User? Leader { get; set; }
            }

            [MapFrom<User>]
            public partial record UserDto
            {
                public int Id { get; init; }
                public TeamDto? Team { get; init; }
            }

            [MapFrom<Team>]
            public partial record TeamDto
            {
                public int Id { get; init; }
                {{leader}}
            }
        }
        """;

    [Fact]
    public void ACycleInTheGraph_ReportsPRAG0313()
    {
        HasDiagnostic(RunGenerator(Cycle("public UserDto? Leader { get; init; }")), "PRAG0313")
            .Should().BeTrue("UserDto reaches TeamDto, which reaches UserDto: FromEntity tracks the visited instances, and says so");
    }

    [Fact]
    public void AGraphWithoutACycle_IsNotReported()
    {
        HasDiagnostic(RunGenerator(Cycle("")), "PRAG0313").Should().BeFalse();
    }

    // ── PRAG0335: [LinkIds] where there is no change tracker to link with ────

    /// <param name="attribute">The attribute on the list of keys, or nothing.</param>
    private static string Links(string attribute) => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;

        namespace TestApp
        {
            public class Label
            {
                public Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            public class Order
            {
                public string Reference { get; set; } = "";
                public ICollection<Label> Labels { get; set; } = new List<Label>();
            }

            [MapTo<Order>]
            public partial class UpdateOrderDto
            {
                public string Reference { get; init; } = "";

                {{attribute}}
                public List<Guid> LabelIds { get; init; } = new();
            }
        }
        """;

    /// <remarks>
    ///     This harness compiles without EF Core, so the tracked form <c>ApplyTo(entity, context)</c>
    ///     is never written: exactly the compilation in which a <c>[LinkIds]</c> can take no effect.
    /// </remarks>
    [Fact]
    public void LinkIdsWithoutTheTrackedForm_ReportsPRAG0335()
    {
        HasDiagnostic(RunGenerator(Links("[LinkIds(nameof(Order.Labels))]")), "PRAG0335")
            .Should().BeTrue("attaching rows by key needs a DbContext, and there is none in this compilation");
    }

    [Fact]
    public void AListOfKeysThatLinksNothing_IsNotReported()
    {
        HasDiagnostic(RunGenerator(Links("")), "PRAG0335").Should().BeFalse();
    }

    // ── PRAG0336: a write aimed at a computed property ────────────────────────

    /// <param name="attribute">The attribute on the DTO's <c>Total</c>, or nothing.</param>
    private static string Computed(string attribute) => $$"""
        using Pragmatic.Mapping;
        using Pragmatic.Mapping.Attributes;

        namespace TestApp
        {
            public class Order
            {
                public decimal Net { get; set; }
                public decimal Tax { get; set; }
                public decimal Total => Net + Tax;
            }

            [MapFrom<Order>]
            [MapTo<Order>]
            public partial class OrderDto
            {
                public decimal Net { get; init; }
                public decimal Tax { get; init; }

                {{attribute}}
                public decimal Total { get; init; }
            }
        }
        """;

    [Fact]
    public void AWriteAimedAtAComputedProperty_ReportsPRAG0336()
    {
        var result = RunGenerator(Computed(""));

        HasDiagnostic(result, "PRAG0336").Should().BeTrue("there is no setter to call, so the write leaves it alone and says so");
        HasCompilationErrors(result).Should().BeFalse(
            "the assignment is not emitted: " + string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
    }

    [Fact]
    public void AnIgnoreForTheWrite_SaysItOutLoud_AndIsNotReported()
    {
        HasDiagnostic(RunGenerator(Computed("[MapIgnore(MappingDirection.ToEntity)]")), "PRAG0336")
            .Should().BeFalse("the remedy the diagnostic names is what the author wrote");
    }
}
