using System.Linq;
using System.Reflection;
using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;
using Pragmatic.Endpoints.Attributes;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class AttributeTests
{
    // --- EndpointAttribute ---

    [Fact]
    public void EndpointAttribute_SetsMethod()
    {
        var attr = new EndpointAttribute(HttpVerb.Post, "/api/orders");

        attr.Method.Should().Be(HttpVerb.Post);
    }

    [Fact]
    public void EndpointAttribute_SetsRoute()
    {
        var attr = new EndpointAttribute(HttpVerb.Get, "/api/users/{id}");

        attr.Route.Should().Be("/api/users/{id}");
    }

    [Fact]
    public void EndpointAttribute_Name_DefaultIsNull()
    {
        var attr = new EndpointAttribute(HttpVerb.Get, "/api/users");

        attr.Name.Should().BeNull();
    }

    /// <summary>
    ///     The attribute goes on a class and on a <b>member</b>: the latter for the specification.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A <c>[Query]</c> on a static member returning <c>Specification&lt;TEntity&gt;</c> derives a
    ///         query, and this attribute next to it gives it the route. With the class as the only target,
    ///         the declaration would be <c>CS0592</c> on the author's line and the rule would stay mute
    ///         over HTTP.
    ///     </para>
    ///     <para>
    ///         ⚠️ Widening the targets alone would make an attribute that can be written anywhere and
    ///         does nothing almost everywhere: <c>PRAG0525</c> is the half that prevents it, and it lives
    ///         in the generator suite because that is where the defect shows.
    ///     </para>
    /// </remarks>
    [Fact]
    public void EndpointAttribute_TargetsAClassOrASpecificationMember()
    {
        var usage = typeof(EndpointAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(
            AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property);
    }

    /// <summary>
    ///     Group membership is not on <c>[Endpoint]</c>: it is on an attribute of its own.
    /// </summary>
    /// <remarks>
    ///     Membership is one idea with one spelling, <c>[EndpointGroup&lt;TGroup&gt;]</c>, so what this
    ///     pins is that <c>[Endpoint]</c> says the verb and the route, and nothing else.
    /// </remarks>
    [Fact]
    public void EndpointAttribute_SaysTheVerbAndTheRoute_AndNotWhereItBelongs()
    {
        var attr = new EndpointAttribute(HttpVerb.Get, "/test");

        attr.Method.Should().Be(HttpVerb.Get);
        attr.Route.Should().Be("/test");

        typeof(EndpointAttribute).GetProperties().Select(p => p.Name)
            .Should().NotContain("Group",
                "membership is declared with [EndpointGroup<TGroup>], and a second spelling would "
                + "make it two things again");
    }

    /// <summary>The same name in two roles, told apart by arity.</summary>
    [Fact]
    public void EndpointGroupAttribute_DeclaresAGroup_AndItsGenericFormJoinsOne()
    {
        var declares = new EndpointGroupAttribute("/api/orders") { Tag = "Orders" };

        declares.RoutePrefix.Should().Be("/api/orders");
        declares.Tag.Should().Be("Orders");

        typeof(EndpointGroupAttribute).GetProperties().Select(p => p.Name)
            .Should().NotContain("Parent",
                "a nested group declares its parent like any other membership");

        typeof(EndpointGroupAttribute<>).GetGenericArguments().Should().HaveCount(1);
    }

    // --- ApiVersionAttribute ---

    [Fact]
    public void ApiVersionAttribute_SetsVersion()
    {
        var attr = new ApiVersionAttribute("2.0");

        attr.Version.Should().Be("2.0");
    }

    [Fact]
    public void ApiVersionAttribute_Deprecated_DefaultIsFalse()
    {
        var attr = new ApiVersionAttribute("1.0");

        attr.Deprecated.Should().BeFalse();
    }

    [Fact]
    public void ApiVersionAttribute_AllowMultiple()
    {
        var usage = typeof(ApiVersionAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        usage!.AllowMultiple.Should().BeTrue();
    }

    [Fact]
    public void ApiVersionAttribute_DeprecationMessage_DefaultIsNull()
    {
        var attr = new ApiVersionAttribute("1.0");

        attr.DeprecationMessage.Should().BeNull();
    }

    // --- EndpointGroupAttribute ---

    [Fact]
    public void EndpointGroupAttribute_SetsRoutePrefix()
    {
        var attr = new EndpointGroupAttribute("/api/v1/orders");

        attr.RoutePrefix.Should().Be("/api/v1/orders");
    }

    [Fact]
    public void EndpointGroupAttribute_Tag_DefaultIsNull()
    {
        var attr = new EndpointGroupAttribute("/api");

        attr.Tag.Should().BeNull();
    }

    // --- RateLimitAttribute ---

    [Fact]
    public void RateLimitAttribute_SetsRequests()
    {
        var attr = new RateLimitAttribute { Requests = 100 };

        attr.Requests.Should().Be(100);
    }

    [Fact]
    public void RateLimitAttribute_SetsWindow()
    {
        var attr = new RateLimitAttribute { Window = "1m" };

        attr.Window.Should().Be("1m");
    }

    [Fact]
    public void RateLimitAttribute_Policy_DefaultIsNull()
    {
        var attr = new RateLimitAttribute();

        attr.Policy.Should().BeNull();
    }

    // --- RequirePermissionAttribute ---

    [Fact]
    public void RequirePermissionAttribute_StoresPermissions()
    {
        var attr = new RequirePermissionAttribute("orders.read", "payments.refund");

        attr.Permissions.Should().BeEquivalentTo("orders.read", "payments.refund");
    }

    [Fact]
    public void RequireAnyPermissionAttribute_StoresPermissions()
    {
        var attr = new RequireAnyPermissionAttribute("reports.sales", "reports.all");

        attr.Permissions.Should().BeEquivalentTo("reports.sales", "reports.all");
    }

    // --- ResponseCacheAttribute ---

    [Fact]
    public void ResponseCacheAttribute_SetsDuration()
    {
        var attr = new Pragmatic.Endpoints.Attributes.ResponseCacheAttribute { Duration = 300 };

        attr.Duration.Should().Be(300);
    }

    [Fact]
    public void ResponseCacheAttribute_DefaultLocation_IsAny()
    {
        var attr = new Pragmatic.Endpoints.Attributes.ResponseCacheAttribute();

        attr.Location.Should().Be(ResponseCacheLocation.Any);
    }

    // --- HttpStatusAttribute ---

    [Fact]
    public void HttpStatusAttribute_SetsStatusCode()
    {
        var attr = new HttpStatusAttribute(402);

        attr.StatusCode.Should().Be(402);
    }

    [Fact]
    public void HttpStatusAttribute_TargetsClassAndStruct()
    {
        var usage = typeof(HttpStatusAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
        usage!.ValidOn.Should().HaveFlag(AttributeTargets.Struct);
    }

    // --- ApiTagsAttribute ---

    [Fact]
    public void ApiTagsAttribute_StoresTags()
    {
        var attr = new ApiTagsAttribute("Orders", "Customer Portal");

        attr.Tags.Should().BeEquivalentTo("Orders", "Customer Portal");
    }

    [Fact]
    public void ApiTagsAttribute_EmptyTags()
    {
        var attr = new ApiTagsAttribute();

        attr.Tags.Should().BeEmpty();
    }
}
