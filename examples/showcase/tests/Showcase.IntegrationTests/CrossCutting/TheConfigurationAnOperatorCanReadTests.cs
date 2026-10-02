using System.Net;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A package's actions are hosted: <c>Pragmatic.Configuration.Management</c> imported
///     into a module, its read exposed, and the host still starts.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The host not starting is what this case is really about.</b> Each of the six
///         package actions generates an invoker that asks for an <c>IUnitOfWork</c>. Reading
///         <c>[BelongsTo&lt;TPackage&gt;]</c> as "belongs to a boundary" would key it by the
///         <em>package</em> type, which nothing registers: container validation would fail at
///         <c>builder.Build()</c>, and every test in this suite with it. So every other test here is
///         also a witness for this one; what the case adds is the route answering.
///     </para>
///     <para>
///         ⚠️ It asserts what the route <b>answers</b>, which is only possible because the handler the
///         host generates for an exposed action binds a GET from the query string, not the request body.
///         <c>Booking:CancellationWindowHours</c> is what <c>ShowcaseConfigurationSeeder</c> writes for
///         <c>premium-hotel</c> at startup, read back through the admin route.
///     </para>
/// </remarks>
public class TheConfigurationAnOperatorCanReadTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Prefix = "Booking";

    /// <summary>
    ///     Inside <c>OperationsGroup</c>: <c>[ExposeEndpoint&lt;GetConfigValues, OperationsGroup&gt;]</c>
    ///     publishes it under the group's prefix.
    /// </summary>
    private const string Route = "/api/operations/configuration/values";

    /// <summary>
    ///     The package's route answers, with the values the prefix names.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The values are asserted, not only "not a 404". The query string is where a GET carries
    ///     its inputs, and the generated handler reads them there: read from the <b>request body</b>
    ///     whatever the verb, a GET would answer <b>415</b> — a client cannot send one. The 404
    ///     assertion stays beside the value: they fail differently, and telling
    ///     "the route is gone" from "the route does not answer" is the reason to keep both.
    /// </remarks>
    [Fact]
    public async Task TheAdminRoute_AnswersWithTheValuesThePrefixNames()
    {
        using var operator1 = CreateClientAsWithPermissions(
            "config-operator", "Config Operator", "configuration.values.read");

        var response = await operator1.GetAsync($"{Route}?prefix={Prefix}");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "[ExposeEndpoint<GetConfigValues>] on AccountsModule publishes it");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a GET binds from the query string: {await response.Content.ReadAsStringAsync()}");

        (await response.Content.ReadAsStringAsync())
            .Should().Contain("Booking:CancellationWindowHours",
                "ShowcaseConfigurationSeeder writes it for premium-hotel at startup, and the prefix "
                + "reached the action — which is what says the query string was bound and not ignored");
    }

    /// <summary>
    ///     The control: the prefix is read, not decoration.
    /// </summary>
    /// <remarks>
    ///     Without it, "the route answers 200 with Booking:…" is satisfied by a handler that ignores
    ///     the query string and dumps everything — which is exactly what an unbound GET that somehow
    ///     ran would do.
    /// </remarks>
    [Fact]
    public async Task APrefixThatNamesNothing_ComesBackEmpty()
    {
        using var operator1 = CreateClientAsWithPermissions(
            "config-operator2", "Config Operator", "configuration.values.read");

        var response = await operator1.GetAsync($"{Route}?prefix=NoSuchSection");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync())
            .Should().NotContain("Booking:CancellationWindowHours",
                "the action filtered by the prefix it was given");
    }

    /// <summary>
    ///     The control: the route requires the package's own permission, so a caller without it is
    ///     refused rather than served.
    /// </summary>
    /// <remarks>
    ///     Without this, the 200 above would also be satisfied by a route open to anyone — and this
    ///     one reads configuration values, which is not a route to leave open.
    /// </remarks>
    [Fact]
    public async Task ACallerWithoutThePermission_IsRefused()
    {
        using var nobody = CreateClientAsWithPermissions(
            "config-nobody", "No Config", "booking.guest.read");

        var response = await nobody.GetAsync($"{Route}?prefix={Prefix}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "configuration.values.read is what the action requires");
    }

    /// <summary>
    ///     The group is where the route is, not decoration: the bare route is gone.
    /// </summary>
    /// <remarks>
    ///     A host that read the group into its model and mapped the route on the root would answer
    ///     here. A 200 at the grouped path alone would not show the group was
    ///     honoured if the root still served it too.
    /// </remarks>
    [Fact]
    public async Task TheRouteOutsideTheGroup_IsNotServed()
    {
        using var operator1 = CreateClientAsWithPermissions(
            "config-operator3", "Config Operator", "configuration.values.read");

        var response = await operator1.GetAsync($"/configuration/values?prefix={Prefix}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the exposed endpoint names OperationsGroup, so it lives under /api/operations only");
    }
}
