using Pragmatic.Testing.Assertions;
using Pragmatic.Result.Http;
using Xunit;

namespace Showcase.Tests.Unit.Errors;

/// <summary>
/// Tests NotFoundError factory methods from Pragmatic.Result.Http.
/// Demonstrates: Standard framework errors replace per-boundary custom copies.
/// </summary>
public class NotFoundErrorTests
{
    [Fact]
    public void For_WithGuid_SetsEntityTypeFromStringParam()
    {
        var id = Guid.NewGuid();

        var error = NotFoundError.For<Guid>("Reservation", id);

        error.EntityType.Should().Be("Reservation");
        error.EntityId.Should().Be(id.ToString());
    }

    [Fact]
    public void For_WithString_SetsEntityId()
    {
        var error = NotFoundError.For("Property", "PROP-001");

        error.EntityType.Should().Be("Property");
        error.EntityId.Should().Be("PROP-001");
    }

    [Fact]
    public void For_Invoice_SetsCorrectType()
    {
        var id = Guid.NewGuid();

        var error = NotFoundError.For<Guid>("Invoice", id);

        error.EntityType.Should().Be("Invoice");
        error.Code.Should().Be("NOT_FOUND");
        error.StatusCode.Should().Be(404);
    }

    [Fact]
    public void For_Different_Entities_ProduceDifferentEntityTypes()
    {
        var id = Guid.NewGuid();

        var guestError = NotFoundError.For<Guid>("Guest", id);
        var propertyError = NotFoundError.For<Guid>("Property", id);

        guestError.EntityType.Should().Be("Guest");
        propertyError.EntityType.Should().Be("Property");
    }
}
