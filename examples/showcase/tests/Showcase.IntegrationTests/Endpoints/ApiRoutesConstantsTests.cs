using Pragmatic.Testing.Assertions;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     W5 (testing two-tier), tier 1 directly: the route constants and typed URL builders the generator writes into the app assembly.
/// </summary>
/// <remarks>
///     They are named here by their full name, <c>Showcase.Booking.ApiRoutes</c>. The generator puts them in the assembly's namespace, and an
///     engine copy of the module is another assembly (<c>Showcase.Booking.Sql</c>), so this file runs on EF Core only. The typed client built
///     on them, <see cref="TypedClientTests" />, runs on every engine (#874).
/// </remarks>
public class ApiRoutesConstantsTests
{
    [Fact]
    public void ApiRoutes_Constants_AreCompileTimeAndTyped()
    {
        Showcase.Booking.ApiRoutes.Booking.CreateGuestMethod.Should().Be("POST");

        var id = Guid.NewGuid();
        var url = Showcase.Booking.ApiRoutes.Guests.GetGuest(id);
        url.Should().Contain(id.ToString());
    }
}
