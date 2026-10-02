using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     A missing permission reaches the client in one shape, whichever layer refuses it:
///     <c>requiredPermissions</c>, always an array, and <c>permissionMatch</c> saying whether all of them or
///     any of them would have been enough — never a single <c>requiredPermission</c> string with
///     several permissions joined by ", " or " | ".
/// </summary>
public class ForbiddenErrorWireShapeTests
{
    [Fact]
    public void OneMissingPermission_IsWrittenAsAnArray()
    {
        var extensions = new Dictionary<string, object?>();

        ForbiddenError.MissingPermission("leave.leave-request.decide").WriteExtensions(extensions);

        extensions.Should().NotContainKey("requiredPermission");
        ((IEnumerable<string>)extensions["requiredPermissions"]!).Should().Equal("leave.leave-request.decide");
    }

    [Fact]
    public void AllOfSeveral_IsWrittenAsTheListAndTheMatch()
    {
        var extensions = new Dictionary<string, object?>();

        ForbiddenError.MissingPermissions(["orders.read", "orders.write"], PermissionMatch.All).WriteExtensions(extensions);

        ((IEnumerable<string>)extensions["requiredPermissions"]!).Should().Equal("orders.read", "orders.write");
        extensions["permissionMatch"].Should().Be("all");
    }

    [Fact]
    public void AnyOfSeveral_SaysAny()
    {
        var extensions = new Dictionary<string, object?>();

        ForbiddenError.MissingPermissions(["orders.read", "orders.admin"], PermissionMatch.Any).WriteExtensions(extensions);

        ((IEnumerable<string>)extensions["requiredPermissions"]!).Should().Equal("orders.read", "orders.admin");
        extensions["permissionMatch"].Should().Be("any");
    }

    [Fact]
    public void TheDetail_NamesWhatWasMissing()
    {
        ForbiddenError.MissingPermission("orders.read").Description
            .Should().Be("The current user is missing the 'orders.read' permission.");
        ForbiddenError.MissingPermissions(["orders.read", "orders.write"], PermissionMatch.All).Description
            .Should().Be("The current user is missing the permissions: orders.read, orders.write.");
        ForbiddenError.MissingPermissions(["orders.read", "orders.admin"], PermissionMatch.Any).Description
            .Should().Be("The current user has none of the permissions: orders.read, orders.admin.");
        ForbiddenError.ActionDenied("delete").Description
            .Should().Be("The current user is not allowed to perform this operation.");
    }

    /// <summary>The control: a refusal that names no permission writes no permission keys.</summary>
    [Fact]
    public void ARefusalWithoutAPermission_WritesNone()
    {
        var extensions = new Dictionary<string, object?>();

        ForbiddenError.ActionDenied("delete", "invoice").WriteExtensions(extensions);

        extensions.Should().NotContainKey("requiredPermissions");
        extensions.Should().NotContainKey("permissionMatch");
        extensions["action"].Should().Be("delete");
    }
}
