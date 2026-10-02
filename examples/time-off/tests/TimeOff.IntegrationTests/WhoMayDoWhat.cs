using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Catalog;
using Pragmatic.Identity.Authorization;
using Pragmatic.Endpoints.Authorization;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using TimeOff.Leave;

namespace TimeOff.IntegrationTests;

/// <summary>
///     What each role may do: every operation of the application, who reaches it, and who is
///     refused with a 403; an anonymous caller is refused with a 401 everywhere but signing in.
/// </summary>
/// <remarks>
///     <para>
///         The table is the rule, written as the product states it — not read back from the
///         permissions, which would test the framework against itself. Each role signs in as a client
///         does and calls every operation.
///     </para>
///     <para>
///         "Reaches" means authorization let the call through: what comes after — a 404 for an id that
///         does not exist, a 422 for an empty body — is the operation answering, and not the subject
///         here. A 401 or 403 is the only way to be refused.
///     </para>
/// </remarks>
public sealed class WhoMayDoWhat(PostgresFixture database) : TimeOffTestBase(database)
{
    private const string Employee = nameof(Employee);
    private const string Manager = nameof(Manager);
    private const string Hr = nameof(Hr);

    private static readonly string[] Everyone = [Employee, Manager, Hr];

    /// <summary>Every operation that needs a signed-in caller, and the roles that may perform it.</summary>
    private static readonly Operation[] Operations =
    [
        new("POST", "api/employees", [Hr]),
        new("GET", "api/employees", [Hr]),
        new("GET", "api/employees/{id}", [Hr]),
        new("PUT", "api/employees/{id}", [Hr]),
        new("DELETE", "api/employees/{id}", [Hr]),
        new("POST", "api/employees/{id}/restore", [Hr]),
        new("POST", "api/employees/{id}/erasure", [Hr]),
        new("POST", "api/employees/{id}/transfer", [Hr]),
        new("POST", "api/teams", [Hr]),
        new("GET", "api/teams", [Hr]),
        new("PUT", "api/teams/{id}", [Hr]),
        new("DELETE", "api/teams/{id}", [Hr]),
        new("POST", "api/teams/{id}/allowances", [Hr]),
        new("POST", "api/absence-kinds", [Hr]),
        new("PUT", "api/absence-kinds/{id}", [Hr]),
        new("DELETE", "api/absence-kinds/{id}", [Hr]),
        new("GET", "api/absence-kinds", Everyone),
        new("POST", "api/allowances", [Hr]),
        new("PUT", "api/allowances/{id}", [Hr]),
        new("DELETE", "api/allowances/{id}", [Hr]),
        new("POST", "api/allowances/carry-over", [Hr]),
        new("POST", "api/company-holidays", [Hr]),
        new("DELETE", "api/company-holidays/{id}", [Hr]),
        new("GET", "api/company-holidays", Everyone),
        new("POST", "api/leave-requests", Everyone),
        new("GET", "api/leave-requests", Everyone),
        new("GET", "api/leave-requests/{id}", Everyone),
        // The reason, decrypted — or the fact that it cannot be. Same permission as reading the
        // request: what protects it is the access scope on the row, not a second permission.
        new("GET", "api/leave-requests/{id}/reason", Everyone),
        new("POST", "api/leave-requests/{id}/approve", [Manager]),
        new("POST", "api/leave-requests/{id}/reject", [Manager]),
        new("POST", "api/leave-requests/{id}/withdraw", Everyone),
        new("GET", "api/leave-requests/{id}/decisions", [Hr]),
        new("GET", "api/team-calendar", Everyone),
        new("GET", "api/me", Everyone),
        new("GET", "api/me/balances", Everyone),
        new("PUT", "api/me/language", Everyone),
        new("GET", "api/me/personal-data", Everyone),
        new("GET", "api/reports/absences", [Hr]),
        new("GET", "api/compliance/processing-register", [Hr]),
        new("GET", "api/compliance/audit-trail/integrity", [Hr]),
        new("GET", "api/compliance/security-incidents", [Hr]),
        new("POST", "identity/local/change-password", Everyone)
    ];

    /// <summary>The operations a caller performs before having an account to sign in with.</summary>
    private static readonly (string Method, string Route)[] Anonymous =
    [
        ("POST", "identity/local/sign-in"),
        ("POST", "identity/local/reset-password/request"),
        ("POST", "identity/local/reset-password/confirm")
    ];

    [Fact]
    public async Task EachRole_ReachesItsOperations_AndIsRefusedTheOthers()
    {
        var clients = new Dictionary<string, HttpClient>
        {
            [Employee] = await SignInAsync((await HireAsync()).Account),
            [Manager] = await SignInAsync((await HireAsync(role: "Manager")).Account),
            [Hr] = await SignInAsHrAsync()
        };

        var wrong = new List<string>();
        foreach (var operation in Operations)
        {
            foreach (var (role, client) in clients)
            {
                var status = (await client.SendAsync(operation.Request())).StatusCode;
                var allowed = operation.Roles.Contains(role);
                if (allowed && status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    wrong.Add($"{role} refused {operation}: {(int)status}");
                if (!allowed && status != HttpStatusCode.Forbidden)
                    wrong.Add($"{role} not refused {operation}: {(int)status}");
            }
        }

        wrong.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAnonymousCaller_IsRefusedWith401_EverywhereButTheWayIn()
    {
        var wrong = new List<string>();
        foreach (var operation in Operations)
        {
            var status = (await Client.SendAsync(operation.Request())).StatusCode;
            if (status != HttpStatusCode.Unauthorized)
                wrong.Add($"anonymous {operation}: {(int)status}");
        }

        foreach (var (method, route) in Anonymous)
        {
            var status = (await Client.SendAsync(new Operation(method, route, []).Request())).StatusCode;
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                wrong.Add($"anonymous refused {method} {route}: {(int)status}");
        }

        wrong.Should().BeEmpty();
    }

    /// <summary>
    ///     The table is every endpoint the application maps, and every one of them declares who may call
    ///     it: an endpoint added without a permission, or left out of the table, fails here.
    /// </summary>
    [Fact]
    public void EveryEndpoint_IsInTheTable_AndDeclaresWhoMayCallIt()
    {
        var mapped = Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Route: e.RoutePattern.RawText?.TrimStart('/') ?? "", Endpoint: e))
            .Where(e => e.Route.StartsWith("api/", StringComparison.Ordinal)
                        || e.Route.StartsWith("identity/", StringComparison.Ordinal))
            .SelectMany(e => (e.Endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(method =>
                (Key: $"{method} {e.Route}", e.Endpoint)))
            .ToList();

        var declared = Operations.Select(o => o.ToString()).Concat(Anonymous.Select(a => $"{a.Method} {a.Route}"));
        mapped.Select(m => m.Key).Should().BeEquivalentTo(declared);

        // A permission, not merely a signed-in caller: an endpoint that asks for nothing more than a token
        // is open to every role there is, and to every role added later.
        var undeclared = mapped
            .Where(m => m.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
                        && !m.Endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
                            .SelectMany(p => p.Requirements)
                            .OfType<PragmaticPermissionRequirement>()
                            .Any())
            .Select(m => m.Key);
        undeclared.Should().BeEmpty("every operation declares the permission it needs, or that it needs none");
    }

    /// <summary>
    ///     The permissions that are no entity's CRUD — declared as <c>[assembly: Permission]</c> lines — are in
    ///     the catalogue a role screen reads, each with the description and category it was declared with.
    /// </summary>
    [Fact]
    public async Task TheCatalogue_ListsTheDeclaredPermissions_WithTheirDescriptions()
    {
        PermissionInfo[] declared =
        [
            new(LeavePermissions.LeaveRequest.Decide, "Approve or reject the leave requests one can see", "Leave"),
            new(LeavePermissions.LeaveRequest.Withdraw, "Withdraw one's own leave request before it starts", "Leave"),
            new(LeavePermissions.OwnProfile.Manage, "See one's own profile, balances and personal data, and choose one's language", "Leave"),
            new(LeavePermissions.Employee.Restore, "Restore an employee who was recorded as having left", "Employee"),
            new(LeavePermissions.PersonalData.Erase, "Erase an employee's personal data", "Privacy"),
            new(LeavePermissions.ProcessingRegister.Read, "Read the register of processing activities", "Privacy"),
            new(LeavePermissions.AuditTrail.Verify, "Verify the integrity of the audit trail", "Privacy")
        ];

        using var scope = Services.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<IPermissionCatalog>().GetAllPermissionsAsync();

        listed.Where(p => declared.Any(d => d.Name == p.Name)).Should().BeEquivalentTo(declared);
    }

    private sealed record Operation(string Method, string Route, string[] Roles)
    {
        public HttpRequestMessage Request()
        {
            var request = new HttpRequestMessage(new HttpMethod(Method), "/" + Route.Replace("{id}", Guid.NewGuid().ToString()));
            if (Method is "POST" or "PUT")
                request.Content = JsonContent.Create(new { });
            return request;
        }

        public override string ToString() => $"{Method} {Route}";
    }
}
