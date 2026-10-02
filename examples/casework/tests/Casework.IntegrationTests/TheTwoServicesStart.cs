using Casework.Verify;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     Both services start on empty databases, each publishes its contract, and each owns its
///     own tables.
/// </summary>
/// <remarks>
///     The skeleton has no operation yet, so neither document lists a route: what is asserted is that
///     both are served, and that the two schemas are **separate**. A single host with two modules starts
///     and serves two documents just as happily, which is why the second test — not the first — is the
///     one that says "two services".
/// </remarks>
public sealed class TheTwoServicesStart(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    [Fact]
    public async Task OnEmptyDatabases_BothStart_AndEachServesItsOwnOpenApiDocument()
    {
        var intake = await ReadJsonAsync(await Intake.GetAsync("/openapi/v1.json"));
        var verify = await ReadJsonAsync(await Verify.GetAsync("/openapi/v1.json"));

        intake.GetProperty("openapi").GetString().Should().StartWith("3.");
        verify.GetProperty("openapi").GetString().Should().StartWith("3.");
    }

    /// <summary>
    ///     The one that matters: each service migrated <b>its own</b> database and nothing of the other's
    ///     is in it. A case and a verification in one schema would be a monolith wearing two hosts.
    /// </summary>
    [Fact]
    public async Task EachService_MigratedItsOwnDatabase_AndKnowsNothingOfTheOthers()
    {
        // Each host migrates at startup; asking each client for its document is what waits for it.
        await ReadJsonAsync(await Intake.GetAsync("/openapi/v1.json"));
        await ReadJsonAsync(await Verify.GetAsync("/openapi/v1.json"));

        var intake = await TablesAsync(IntakeConnectionString);
        var verify = await TablesAsync(VerifyConnectionString);

        intake.Should().Contain("Cases", "Intake's entity is on Intake's database");
        intake.Should().NotContain("Verifications", "and Verify's is not: the two services own their data");

        verify.Should().Contain("Verifications", "Verify's entity is on Verify's database");
        verify.Should().NotContain("Cases", "and Intake's is not");
    }

    /// <summary>
    ///     Spec 7.25 in the real container: all four interfaces a boundary with a group generates are
    ///     handed out, and the permission distinction is drawn by <b>which instance</b> answers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>All four are registered, the group's internal twin included.</b> Were
    ///         <c>IVerifyVerificationsInternalActions</c> missing, injecting it would answer "No service
    ///         for type … has been registered" — and in a message handler that means the handler cannot
    ///         be constructed, the message is nacked and dropped, and there is nothing to read but a
    ///         timeout.
    ///     </para>
    ///     <para>
    ///         So what is asserted here is not "four resolve" — that is satisfied by registering four
    ///         doors into the same unguarded room, which is exactly the defect to avoid. It is
    ///         the <b>instance</b> each interface answers with: the internal twin is the very object the
    ///         root hands out, and the group's <em>public</em> interface is a <b>different</b> one, the
    ///         guarded twin that lets the invoked operation's permission be asked. That pair is spec
    ///         7.25, and neither half means anything alone.
    ///     </para>
    ///     <para>
    ///         Asserted by <b>resolving</b> rather than by reading the generated registration: a
    ///         registration naming a type the container cannot build is green in the text and throws
    ///         here.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Verify_HandsOutAllFourActionInterfaces_AndTheGuardedOneIsADifferentInstance()
    {
        using var scope = VerifyServices.CreateScope();
        var services = scope.ServiceProvider;

        services.GetService<IVerifyActions>().Should().NotBeNull("the root's public interface");

        var root = services.GetService<IVerifyInternalActions>();
        root.Should().NotBeNull("the root's internal interface");

        var twin = services.GetService<IVerifyVerificationsInternalActions>();
        twin.Should().NotBeNull(
            "a caller with no principal — a message handler, a job — injects the group's twin by name");

        var publicGroup = services.GetService<IVerifyVerificationsActions>();
        publicGroup.Should().NotBeNull("the group's public interface");

        // The control that carries the rule: same object as the root's, and NOT the guarded one.
        twin.Should().BeSameAs(root!.Verifications,
            "injecting the twin and going through the root are one instance, not two");
        publicGroup.Should().NotBeSameAs(twin,
            "the group's public interface answers with the guarded twin: the distinction is the instance, "
            + "not the interface, and four registrations onto one unguarded object would be the defect");
    }
}
