using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for EndpointsSourceGenerator covering Mutation-based endpoint scenarios.
///     Verifies that mutations get their own pipeline with IMutationInvoker.
/// </summary>
public class MutationEndpointTests : EndpointsGeneratorTestBase
{
    // =========================================================================
    // Basic Mutation Endpoint Tests
    // =========================================================================

    [Fact]
    public void MutationEndpoint_GeneratesHandlerWithMutationInvoker()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Items;

            public class Item
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = "";
            }

            [Endpoint(HttpVerb.Put, "/items/{id}")]
            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateItemMutation : Mutation<Item>
            {
                public required Guid Id { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Mutation uses IMutationInvoker, NOT IDomainActionInvoker
        handlerSource.Should().Contain("IMutationInvoker");
        handlerSource.Should().NotContain("IDomainActionInvoker");
        handlerSource.Should().Contain("invoker.InvokeAsync");
        handlerSource.Should().Contain("MapPut");
        handlerSource.Should().Contain("/items/{id}");
    }

    [Fact]
    public void MutationEndpoint_GeneratesCorrectInvokerTypeParameters()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public class Order
            {
                public Guid Id { get; set; }
                public string Status { get; set; } = "";
            }

            [Endpoint(HttpVerb.Post, "/orders/{id}/confirm")]
            [Mutation(Mode = MutationMode.Update)]
            public partial class ConfirmOrderMutation : Mutation<Order>
            {
                public required Guid Id { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Invoker type should reference both the mutation and entity types
        handlerSource.Should().Contain("IMutationInvoker<global::TestApp.Orders.ConfirmOrderMutation, global::TestApp.Orders.Order>");
    }

    // =========================================================================
    // Error Type Tests
    // =========================================================================

    [Fact]
    public void MutationEndpoint_WithErrorTypes_GeneratesProducesMetadata()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;
            using Pragmatic.Result.Http;

            namespace TestApp.Orders;

            public class Order
            {
                public Guid Id { get; set; }
            }

            [Endpoint(HttpVerb.Post, "/orders/{id}/cancel")]
            [Mutation(Mode = MutationMode.Update)]
            public partial class CancelOrderMutation : Mutation<Order, ConflictError>
            {
                public required Guid Id { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Should produce ConflictError metadata (409)
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(409, typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails)");
    }

    // =========================================================================
    // Body Property Tests
    // =========================================================================

    [Fact]
    public void MutationEndpoint_WithSingleScalarBodyProperty_GeneratesBodyDto()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public class Order
            {
                public Guid Id { get; set; }
                public string Reason { get; set; } = "";
            }

            [Endpoint(HttpVerb.Post, "/orders/{id}/cancel")]
            [Mutation(Mode = MutationMode.Update)]
            public partial class CancelOrderMutation : Mutation<Order>
            {
                public required Guid Id { get; init; }
                public required string Reason { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Single SCALAR body property → generates DTO wrapper (scalars can't be [FromBody] directly)
        handlerSource.Should().Contain("TestApp.Orders.CancelOrderMutationBody body");
        handlerSource.Should().Contain("Reason = body.Reason");
    }

    [Fact]
    public void MutationEndpoint_WithSingleComplexBodyProperty_UsesDirectParam()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public class Order
            {
                public Guid Id { get; set; }
                public byte[] Data { get; set; } = Array.Empty<byte>();
            }

            [Endpoint(HttpVerb.Put, "/orders/{id}/data")]
            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateOrderDataMutation : Mutation<Order>
            {
                public required Guid Id { get; init; }
                public required byte[] Data { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Single COMPLEX body property → passed directly (no DTO wrapper)
        handlerSource.Should().Contain("byte[] data");
        handlerSource.Should().Contain("Data = data");
        handlerSource.Should().NotContain("Body body");
    }

    [Fact]
    public void MutationEndpoint_WithMultipleBodyProperties_GeneratesBodyDto()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public class Order
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = "";
                public decimal Price { get; set; }
            }

            [Endpoint(HttpVerb.Put, "/orders/{id}")]
            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateOrderMutation : Mutation<Order>
            {
                public required Guid Id { get; init; }
                public required string Name { get; init; }
                public required decimal Price { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);
        // Should generate a body DTO when multiple body properties
        sources.Keys.Should().Contain(k => k.Contains("Body"));
    }

    // =========================================================================
    // Response Handling Tests
    // =========================================================================

    /// <summary>
    ///     An update that declares nothing to answer answers nothing: 204. The entity is the
    ///     persistence shape, loaded only as far as the mutation writes — an unloaded collection went on the
    ///     wire as <c>[]</c>. Owner decision: the entity is never the default answer.
    /// </summary>
    [Fact]
    public void AnUpdateDeclaringNoAnswer_Answers204_NotTheEntity()
    {
        var handlerSource = GetGeneratedSource(RunGenerator(ItemMutation("Update", "Put", "/items/{id}")), "Endpoint");

        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("Results.NoContent()");
        handlerSource.Should().NotContain("Results.Ok(success)");
        handlerSource.Should().NotContain("GeneratedJsonResponse<");
        handlerSource.Should().Contain("MapError(error, httpContext)");
    }

    /// <summary>A create of an entity that declares nothing answers the id of the row it made: 201 with <c>{"id": …}</c>.</summary>
    [Fact]
    public void ACreateDeclaringNoAnswer_Answers201_WithTheId()
    {
        var handlerSource = GetGeneratedSource(
            RunGeneratorWithPersistence(ItemMutation("Create", "Post", "/items", entity: true)), "Endpoint");

        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("new global::TestApp.Items.ItemMutation.IdResponse { Id = success.PersistenceId }");
        handlerSource.Should().Contain("Results.Created(");
        handlerSource.Should().NotContain(", success)");
    }

    /// <summary>A create of a type that is not an entity has no id to give: it answers nothing, not the object.</summary>
    [Fact]
    public void ACreateOfANonEntity_DeclaringNoAnswer_Answers204()
    {
        var handlerSource = GetGeneratedSource(RunGenerator(ItemMutation("Create", "Post", "/items")), "Endpoint");

        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("Results.NoContent()");
        handlerSource.Should().NotContain("success.PersistenceId");
    }

    /// <summary>The control: <c>ReturnType = Entity</c> said in so many words still answers the entity.</summary>
    [Fact]
    public void AnUpdateDeclaringEntity_StillAnswersTheEntity()
    {
        var handlerSource = GetGeneratedSource(
            RunGenerator(ItemMutation("Update", "Put", "/items/{id}", ", ReturnType = MutationReturnType.Entity")), "Endpoint");

        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("GeneratedJsonResponse<global::TestApp.Items.Item>(success!, 200,");
        handlerSource.Should().Contain("ProducesResponseTypeMetadata(200, typeof(global::TestApp.Items.Item))");
    }

    private static string ItemMutation(string mode, string verb, string route, string extra = "", bool entity = false) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Items;

        {{(entity ? "[Pragmatic.Persistence.Entity.Entity]" : "")}}
        public partial class Item
        {
            public string Name { get; set; } = "";
        }

        [Endpoint(HttpVerb.{{verb}}, "{{route}}")]
        [Mutation(Mode = MutationMode.{{mode}}{{extra}})]
        public partial class ItemMutation : Mutation<Item>
        {
            public Guid Id { get; init; }
        }
        """;

    // =========================================================================
    // Diagnostic Tests
    // =========================================================================

    [Fact]
    public void MutationEndpoint_NoPRAG0501_ForValidMutation()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Items;

            public class Item
            {
                public Guid Id { get; set; }
            }

            [Endpoint(HttpVerb.Put, "/items/{id}")]
            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateItemMutation : Mutation<Item>
            {
                public required Guid Id { get; init; }
            }
            """;

        var result = RunGenerator(source);

        // PRAG0501 should NOT fire for valid mutations
        HasDiagnostic(result, "PRAG0501").Should().BeFalse();
    }

    // =========================================================================
    // Registration Tests
    // =========================================================================

    [Fact]
    public void MutationEndpoint_IncludedInRegistration()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Items;

            public class Item
            {
                public Guid Id { get; set; }
            }

            [Endpoint(HttpVerb.Put, "/items/{id}")]
            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateItemMutation : Mutation<Item>
            {
                public required Guid Id { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);
        // Should be included in endpoint registration
        sources.Keys.Should().Contain(k => k.Contains("Registration"));
        var registration = GetGeneratedSource(result, "Endpoints.Registration");
        registration.Should().Contain("UpdateItemMutation");
    }
}
