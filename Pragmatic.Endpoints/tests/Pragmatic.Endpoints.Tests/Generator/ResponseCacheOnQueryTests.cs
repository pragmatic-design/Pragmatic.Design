using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Verifies that [ResponseCache] and [RateLimit] configuration propagates correctly
///     to Query handler templates (QueryHandlerTemplate).
/// </summary>
public class ResponseCacheOnQueryTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void Query_WithResponseCache_GeneratesCacheOutput()
    {
        var source = """
            using System.Linq;
            using System.Linq.Expressions;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp.Queries;

            public class Guest
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            public class GuestDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";

                public static Expression<Func<Guest, GuestDto>> Projection =>
                    g => new GuestDto { Id = g.Id, Name = g.Name };
            }

            [Query<Guest, GuestDto>]
            [Endpoint(HttpVerb.Get, "/guests")]
            [ResponseCache(Duration = 120)]
            public partial class SearchGuestsQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """;

        var result = RunGeneratorWithPersistence(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("MapGet");
        handlerSource.Should().Contain("CacheOutput");
        handlerSource.Should().Contain("FromSeconds(120)");
    }

    [Fact]
    public void Query_WithRateLimitPolicy_GeneratesRequireRateLimiting()
    {
        var source = """
            using System.Linq;
            using System.Linq.Expressions;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp.Queries;

            public class Room
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            public class RoomDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";

                public static Expression<Func<Room, RoomDto>> Projection =>
                    r => new RoomDto { Id = r.Id, Name = r.Name };
            }

            [Query<Room, RoomDto>]
            [Endpoint(HttpVerb.Get, "/rooms")]
            [RateLimit(Policy = "search-limit")]
            public partial class SearchRoomsQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """;

        var result = RunGeneratorWithPersistence(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("RequireRateLimiting(\"search-limit\")");
    }

    [Fact]
    public void Query_WithResponseCacheNoStore_DoesNotGenerateCacheOutput()
    {
        var source = """
            using System.Linq;
            using System.Linq.Expressions;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp.Queries;

            public class Order
            {
                public int Id { get; set; }
                public decimal Total { get; set; }
            }

            public class OrderDto
            {
                public int Id { get; set; }
                public decimal Total { get; set; }

                public static Expression<Func<Order, OrderDto>> Projection =>
                    o => new OrderDto { Id = o.Id, Total = o.Total };
            }

            [Query<Order, OrderDto>]
            [Endpoint(HttpVerb.Get, "/orders")]
            [ResponseCache(NoStore = true)]
            public partial class GetOrdersQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """;

        var result = RunGeneratorWithPersistence(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // NoStore must NOT enable server output caching...
        handlerSource.Should().NotContain("CacheOutput");
        // ...and MUST emit an explicit Cache-Control: no-store directive, or it is a silent no-op.
        handlerSource.Should().Contain("CacheControl");
        handlerSource.Should().Contain("\"no-store\"");
    }

    [Fact]
    public void Query_WithResponseCacheLocationClient_GeneratesPrivateCacheControl()
    {
        var source = """
            using System.Linq;
            using System.Linq.Expressions;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp.Queries;

            public class Preference
            {
                public int Id { get; set; }
                public string Value { get; set; } = "";
            }

            public class PreferenceDto
            {
                public int Id { get; set; }
                public string Value { get; set; } = "";

                public static Expression<Func<Preference, PreferenceDto>> Projection =>
                    p => new PreferenceDto { Id = p.Id, Value = p.Value };
            }

            [Query<Preference, PreferenceDto>]
            [Endpoint(HttpVerb.Get, "/preferences")]
            [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]
            public partial class GetPreferencesQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """;

        var result = RunGeneratorWithPersistence(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Location.Client is a per-user (private) directive — it must NOT be cached
        // server-side, and must emit "private, max-age=N" instead.
        handlerSource.Should().NotContain("CacheOutput");
        handlerSource.Should().Contain("\"private, max-age=60\"");
    }

    [Fact]
    public void Query_WithoutCrosscut_DoesNotGenerateCrosscutConfig()
    {
        var source = """
            using System.Linq;
            using System.Linq.Expressions;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp.Queries;

            public class Product
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            public class ProductDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";

                public static Expression<Func<Product, ProductDto>> Projection =>
                    p => new ProductDto { Id = p.Id, Name = p.Name };
            }

            [Query<Product, ProductDto>]
            [Endpoint(HttpVerb.Get, "/products")]
            public partial class SearchProductsQuery
            {
                public int Page { get; set; } = 1;
                public int PageSize { get; set; } = 20;
            }
            """;

        var result = RunGeneratorWithPersistence(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().NotContain("RequireRateLimiting");
        handlerSource.Should().NotContain("CacheOutput");
    }
}
