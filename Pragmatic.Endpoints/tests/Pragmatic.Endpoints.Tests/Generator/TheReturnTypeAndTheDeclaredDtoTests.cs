using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A mutation that declares both <c>[ReturnsDto&lt;T&gt;]</c> and a <c>ReturnType</c> other than
///     <c>Entity</c> says two things about its response. PRAG0535 says which one answers.
/// </summary>
/// <remarks>
///     The <c>ReturnType</c> wins: the handler answers the key record and never reaches the DTO's
///     <c>FromEntity</c>. Without a diagnostic the author would write
///     <c>[ReturnsDto&lt;OrderDto&gt;]</c> and get <c>{"id": …}</c> on the wire.
/// </remarks>
public class TheReturnTypeAndTheDeclaredDtoTests : EndpointsGeneratorTestBase
{
    private static string Source(string mutationAttribute, string dtoAttribute = "[MapFrom<Item>]") => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace TestApp.Items;

        public class Item
        {
            public Guid Id { get; set; }

            [LogicKey]
            public string Code { get; set; } = "";

            public string Name { get; set; } = "";
        }

        {{dtoAttribute}}
        public partial class ItemDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
        }

        [Endpoint(HttpVerb.Post, "/items")]
        {{mutationAttribute}}
        [ReturnsDto<ItemDto>]
        public partial class CreateItemMutation : Mutation<Item>
        {
            public required string Code { get; init; }
            public required string Name { get; init; }
        }
        """;

    [Theory]
    [InlineData("Id")]
    [InlineData("LogicalKey")]
    public void AReturnTypeOtherThanEntity_BesideReturnsDto_ReportsPRAG0535(string returnType)
    {
        var result = Run(Source($"[Mutation(Mode = MutationMode.Create, ReturnType = MutationReturnType.{returnType})]"));

        var diagnostic = GetDiagnosticsById(result, "PRAG0535").Should().ContainSingle().Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        var message = diagnostic.GetMessage();
        message.Should().Contain("ItemDto");
        message.Should().Contain("CreateItemMutation");
        message.Should().Contain($"ReturnType = {returnType}");
    }

    /// <summary>
    ///     A dead DTO is not asked to map from the entity: PRAG0531's advice — add <c>[MapFrom]</c> —
    ///     would have the author fix a declaration nothing reads.
    /// </summary>
    [Fact]
    public void ADeadDtoWithoutMapFrom_ReportsOnlyPRAG0535()
    {
        var result = Run(Source(
            "[Mutation(Mode = MutationMode.Create, ReturnType = MutationReturnType.Id)]", dtoAttribute: ""));

        HasDiagnostic(result, "PRAG0535").Should().BeTrue();
        HasDiagnostic(result, "PRAG0531").Should().BeFalse();
    }

    /// <summary>The control: the DTO alone reports nothing and is what the endpoint answers.</summary>
    [Fact]
    public void ReturnsDtoAlone_ReportsNothing_AndAnswersTheDto()
    {
        var result = Run(Source("[Mutation(Mode = MutationMode.Create)]"));

        HasDiagnostic(result, "PRAG0535").Should().BeFalse();
        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler!.Should().Contain("global::TestApp.Items.ItemDto.FromEntity(success)");
    }

    private static SourceGenRunResult Run(string source)
    {
        var result = RunGenerator(source,
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.LogicKeyAttribute>(),
            GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapFromAttribute<object>>());

        // The source has to be the one the test describes: an unresolved [ReturnsDto] reads as "no DTO",
        // and the absence of a diagnostic would then hold for that reason instead.
        var errors = GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("TestSource") == true)
            .Select(d => d.ToString())
            .ToList();
        errors.Should().BeEmpty(string.Join(" | ", errors));
        return result;
    }
}
