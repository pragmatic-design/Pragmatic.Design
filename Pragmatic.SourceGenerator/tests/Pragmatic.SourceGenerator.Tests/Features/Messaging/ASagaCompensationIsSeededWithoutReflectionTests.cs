using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     The orchestrator the generator writes for a saga with <c>[CompensateWith&lt;T&gt;]</c> seeds the compensator from the saga's state
///     without reflection (#532): it calls nothing marked <c>[RequiresUnreferencedCode]</c> or <c>[RequiresDynamicCode]</c>, which is what the
///     trim and AOT analyzers report as IL2026 and IL3050. It used to round-trip the saga through JSON with a <c>DefaultJsonTypeInfoResolver</c>.
/// </summary>
/// <remarks>
///     The orchestrator is compiled against the saga runtime and every member it names is bound, so a call the analyzers would flag is a call
///     this test finds. The compensator has a <c>required</c> member the saga does not have: the seeding must still compile and leave it at
///     its default, as the JSON round trip did by clearing <c>IsRequired</c>.
/// </remarks>
public class ASagaCompensationIsSeededWithoutReflectionTests
{
    private const string Saga = """
        global using System;
        global using System.Threading;
        global using System.Threading.Tasks;
        using Pragmatic.Messaging.Attributes;
        using Pragmatic.Messaging.Saga;

        namespace App.Sagas;

        public enum StayState { Booked, Confirmed, Done }

        public sealed record StayBooked(Guid StayId) : ICorrelatedMessage { public string CorrelationId => StayId.ToString(); }

        public sealed record StayConfirmed(Guid StayId) : ICorrelatedMessage { public string CorrelationId => StayId.ToString(); }

        public sealed record ConfirmStay(Guid StayId);

        public sealed record PrepareRoom(Guid StayId);

        /// <summary>Seeded from the saga: StayId by its constructor, Nights by its setter, Reason left at its default.</summary>
        public sealed record CancelStay(Guid StayId)
        {
            public int Nights { get; init; }
            public required string Reason { get; init; }
        }

        [Saga<StayState>]
        public partial class StaySaga : ISaga<StayState>
        {
            public Guid Id { get; set; }
            public StayState State { get; set; }
            public string CorrelationId { get; set; } = "";
            public DateTimeOffset StartedAt { get; set; }
            public DateTimeOffset? CompletedAt { get; set; }
            public Guid StayId { get; set; }
            public int Nights { get; set; }

            [SagaStart]
            public ConfirmStay WhenBooked(StayBooked booked) { StayId = booked.StayId; return new ConfirmStay(booked.StayId); }

            [InState(StayState.Booked, NextState = StayState.Confirmed)]
            [CompensateWith<CancelStay>]
            public PrepareRoom WhenConfirmed(StayConfirmed confirmed) => new(StayId);
        }
        """;

    private static (Compilation Output, SyntaxTree Orchestrator) Run()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Saga,
            GeneratorTestHelper.FromType<global::Pragmatic.Messaging.Attributes.CompensateWithAttribute<object>>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Messaging.Saga.ISagaRepository<object>>(),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Text.Json.JsonSerializer)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Text.Encodings.Web.JavaScriptEncoder)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Diagnostics.ActivitySource)),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Serialization.PragmaticJsonTypeInfo)));
        var orchestrator = result.OutputCompilation.SyntaxTrees.Single(t => t.FilePath.EndsWith("StaySaga.Orchestrator.g.cs", StringComparison.Ordinal));
        return (result.OutputCompilation, orchestrator);
    }

    /// <summary>The control: the orchestrator compiles, so every member it names is bound and none of the checks below reads an error symbol.</summary>
    [Fact]
    public void TheOrchestrator_Compiles()
    {
        var (output, orchestrator) = Run();

        output.GetSemanticModel(orchestrator).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        orchestrator.ToString().Should().Contain("DispatchCompensation_WhenConfirmed", "the control: the step's compensation is there");
    }

    /// <summary>The setpoint: nothing the orchestrator calls or constructs needs unreferenced code or dynamic code.</summary>
    [Fact]
    public void TheOrchestrator_CallsNothingTheTrimOrAotAnalyzersFlag()
    {
        var (output, orchestrator) = Run();
        var model = output.GetSemanticModel(orchestrator);

        var flagged = orchestrator.GetRoot().DescendantNodes()
            .Where(n => n is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or MemberAccessExpressionSyntax)
            .Select(n => model.GetSymbolInfo(n).Symbol)
            .Where(s => s is not null && s.GetAttributes().Any(a => a.AttributeClass?.Name is "RequiresUnreferencedCodeAttribute" or "RequiresDynamicCodeAttribute"))
            .Select(s => s!.ToDisplayString())
            .Distinct()
            .ToList();

        flagged.Should().BeEmpty();
    }
}
