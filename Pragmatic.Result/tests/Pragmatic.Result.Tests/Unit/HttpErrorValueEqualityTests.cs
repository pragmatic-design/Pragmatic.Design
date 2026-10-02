using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     The HTTP errors are records, and equal by what they say. Five of them cached their
///     parameters in a private <c>Lazy</c> field, which a record's synthesized equality compares like any
///     other field: no two of them were ever equal, and a <c>with</c> copy kept the original's parameters.
/// </summary>
public class HttpErrorValueEqualityTests
{
    public static TheoryData<IError, IError> SameValues => new()
    {
        { NotFoundError.Create("User", "1"), NotFoundError.Create("User", "1") },
        { ConflictError.AlreadyExists("User", "ada@example.com"), ConflictError.AlreadyExists("User", "ada@example.com") },
        { BadRequestError.Create("bad"), BadRequestError.Create("bad") },
        { new DependencyError { ServiceName = "payments", Reason = "down" }, new DependencyError { ServiceName = "payments", Reason = "down" } },
        { ForbiddenError.MissingPermissions(["a", "b"], PermissionMatch.Any), ForbiddenError.MissingPermissions(["a", "b"], PermissionMatch.Any) },
    };

    [Theory]
    [MemberData(nameof(SameValues))]
    public void TheSameValues_AreEqual_AndHashAlike(IError first, IError second)
    {
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void AWithCopy_CarriesItsOwnParameters()
    {
        var original = NotFoundError.Create("User", "1");
        _ = original.Parameters;

        var copy = original with { EntityId = "2" };

        copy.Parameters!["entityId"].Should().Be("2");
    }

    /// <summary>The control: what differs stays unequal, the permission's order and match included.</summary>
    [Fact]
    public void DifferentValues_StayUnequal()
    {
        NotFoundError.Create("User", "1").Should().NotBe(NotFoundError.Create("User", "2"));
        ForbiddenError.MissingPermissions(["a", "b"], PermissionMatch.Any)
            .Should().NotBe(ForbiddenError.MissingPermissions(["a", "b"], PermissionMatch.All));
        ForbiddenError.MissingPermissions(["a", "b"], PermissionMatch.All)
            .Should().NotBe(ForbiddenError.MissingPermissions(["a", "c"], PermissionMatch.All));
    }
}
