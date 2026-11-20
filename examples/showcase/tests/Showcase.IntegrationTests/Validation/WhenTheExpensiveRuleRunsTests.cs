using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Showcase.Booking;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Validation;

/// <summary>
///     <c>[AsyncValidate&lt;T&gt;]</c> bound to a property: the rule runs when that
///     property changes, and is skipped when it does not.
/// </summary>
/// <remarks>
///     <para>
///         <c>GuestEmailIsFreeValidator</c> asks whether another guest already holds the address.
///         It is a query over rows the operation never loads, so it costs something, and it has
///         nothing to say about an update that changes a phone number.
///     </para>
///     <para>
///         ⚠️ <b>The skip is the half that is hard to see.</b> "The rule refuses a duplicate" is
///         satisfied just as well by a rule that runs on every write, so the second test puts the
///         guest in a state the rule rejects — writing the duplicate address <b>through the
///         repository</b>, past the mutation and its validators — and then changes the phone. It goes through only because
///         <c>Email</c> is not among the modified properties. Asserting that a <i>valid</i> update
///         succeeds would prove nothing: it succeeds either way.
///     </para>
///     <para>
///         Measured by removal: with <c>[AsyncValidate&lt;GuestEmailIsFreeValidator&gt;]</c>
///         off <c>Guest.Email</c>, exactly one of the two goes red — the skip. The refusal stays
///         green, because <c>[Validator]</c> registers the rule and the pipeline runs it on every
///         write once nothing says when. So the first test here is the <b>control</b>, not the
///         measure: it says the rule works, and only the second says the binding does.
///     </para>
/// </remarks>
public class WhenTheExpensiveRuleRunsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ChangingTheAddressToOneAlreadyTaken_IsRefused()
    {
        var taken = $"taken.{Guid.NewGuid():N}@test.com";
        await AGuestAsync(taken);
        var (otherId, otherEmail) = await AGuestAsync($"other.{Guid.NewGuid():N}@test.com");

        var response = await PutAsync($"/api/guests/{otherId}", new { email = taken });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "the address belongs to somebody else, and the bound validator is what asks — body was "
            + await response.Content.ReadAsStringAsync());

        (await StoredEmailAsync(otherId)).Should().Be(otherEmail, "and nothing was written");
    }

    /// <summary>
    ///     The point of binding the rule to a property: an update that does not touch the address
    ///     does not pay for the rule — and here, does not trip over it either.
    /// </summary>
    [Fact]
    public async Task ChangingSomethingElse_DoesNotRunTheRuleAtAll()
    {
        var taken = $"dup.{Guid.NewGuid():N}@test.com";
        await AGuestAsync(taken);
        var (otherId, _) = await AGuestAsync($"other.{Guid.NewGuid():N}@test.com");

        // Past the mutation: the row now holds an address the validator would refuse.
        await ForceEmailAsync(otherId, taken);

        var response = await PutAsync($"/api/guests/{otherId}", new { phone = "+390100000000" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "Email is not among the modified properties, so the validator bound to it never runs — "
            + "body was " + await response.Content.ReadAsStringAsync());

        (await StoredEmailAsync(otherId)).Should().Be(taken,
            "and the duplicate is still there: the update was about the phone");
    }

    private async Task<(Guid Id, string Email)> AGuestAsync(string email)
    {
        var created = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Rule",
            lastName = "Binding",
            email
        });

        return (created.GetProperty("id").GetGuid(), email);
    }

    /// <summary>
    ///     Writes the address through the repository and the unit of work, which run no validator: only the mutation's pipeline does. The
    ///     row filters are lifted because outside a request there is no caller to own the row.
    /// </summary>
    private async Task ForceEmailAsync(Guid guestId, string email)
    {
        using var tenant = TenantScope.BeginScope("test-tenant");
        using var scope = Services.CreateScope();
        using var filters = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>().DisableAll();

        var guests = scope.ServiceProvider.GetRequiredService<IRepository<Guest>>();
        var guest = await guests.GetByIdAsync(guestId) ?? throw new InvalidOperationException($"no guest {guestId}");
        guest.SetEmail(email);
        guests.Update(guest);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(BookingBoundary)).SaveChangesAsync();
    }

    private async Task<string?> StoredEmailAsync(Guid guestId)
    {
        var guest = await GetAsync<JsonElement>($"/api/guests/{guestId}");

        return guest.GetProperty("email").GetString();
    }
}
