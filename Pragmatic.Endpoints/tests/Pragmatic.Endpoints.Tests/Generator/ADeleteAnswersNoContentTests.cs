using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A Delete mutation exposed over HTTP answers 204 and no body, unless it declares what to
///     answer.
/// </summary>
/// <remarks>
///     <para>
///         <c>EndpointModel.ComputedSuccessStatusCode</c> said "Delete mutations are void and fall into
///         204", and nothing made them void: every mutation answered its entity. A deleted row is not an
///         answer anyone asked for, and serialised it is every public property of it — for Time off's
///         employee, the account's password hash and security stamp.
///     </para>
///     <para>
///         <c>[ReturnsDto&lt;T&gt;]</c> still says what goes on the wire, on a delete as on any other
///         mutation.
///     </para>
/// </remarks>
public class ADeleteAnswersNoContentTests : EndpointsGeneratorTestBase
{
    private static string Source(string returnsDto = "") => $$"""
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
            public string Name { get; set; } = "";
            public string Secret { get; set; } = "";
        }

        [MapFrom<Item>]
        public partial class ItemDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
        }

        [Endpoint(HttpVerb.Delete, "/items/{id}")]
        [Mutation(Mode = MutationMode.Delete)]
        {{returnsDto}}
        public partial class DeleteItemMutation : Mutation<Item>
        {
            public required Guid Id { get; init; }
        }
        """;

    [Fact]
    public void ADelete_AnswersNoContent_AndNotTheRow()
    {
        var handler = Handler(Run(Source()));

        handler.Should().Contain("Results.NoContent()");
        handler.Should().NotContain("Results.Ok(success)");
        handler.Should().NotContain("Results.Json(success");
    }

    /// <summary>The control: a delete that declares a DTO answers it.</summary>
    [Fact]
    public void ADeleteThatDeclaresADto_AnswersTheDto()
    {
        var handler = Handler(Run(Source("[ReturnsDto<ItemDto>]")));

        handler.Should().Contain("global::TestApp.Items.ItemDto.FromEntity(success)");
        handler.Should().NotContain("Results.NoContent()");
    }

    private static string Handler(SourceGenRunResult result)
    {
        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        return handler!;
    }

    private static SourceGenRunResult Run(string source)
        => RunGenerator(source,
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.LogicKeyAttribute>(),
            GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapFromAttribute<object>>());
}
