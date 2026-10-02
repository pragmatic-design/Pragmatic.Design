namespace TimeOff.Leave.Infrastructure.Identity;

/// <summary>
///     What a Time off token says about the employee who signed in: their reference as the subject, their
///     name, their access role, and one role per team they manage.
/// </summary>
/// <remarks>
///     <para>
///         The credentials are checked, and the token signed, by the identity package's sign-in; this is the
///         part only Time off knows. The subject is the employee's reference in the subject registry, not
///         their email: the subject is what the application records as the one who acted — the rows'
///         authors, the access scopes, every audit entry — so a pseudonym there means the trail, which is
///         append-only, holds no email that an erasure could not reach.
///     </para>
///     <para>
///         Roles, not permissions: the host maps each role to its permissions on every request, so changing
///         what a role may do needs no new token. A team role (<see cref="TeamScopes" />) maps to no
///         permission; it is the scope that makes the team's requests visible. What invalidates the roles —
///         a new access role, a team handed to another manager — rotates the stamp the token carries.
///     </para>
///     <para>
///         Registered by the host, next to the subject registry it needs: that registry is the host's, and
///         the module's generator, which sees only the module, would report it as not registered.
///     </para>
/// </remarks>
public sealed class EmployeeClaims(
    EmployeeResolver employees,
    IReadRepository<Team> teams,
    ISubjectRegistry subjects) : IUserClaimsContributor
{
    public async ValueTask ContributeAsync(LocalIdentity identity, SignInClaims claims, CancellationToken ct = default)
    {
        // By the key of the account being signed in: nobody is authenticated yet, so the current-user
        // lookup would find no one.
        var employee = await employees.FindByIdentityKeyAsync(identity.ExternalIdentityKey, ct).ConfigureAwait(false);

        // Every account is opened with its employee; one without would get a token naming the account and
        // holding no role, which reaches nothing.
        if (employee is null)
            return;

        var managed = await teams.FindManagedByAsync(employee.Id, ct).ConfigureAwait(false);

        claims.Subject = await employee.ReferenceAsync(subjects, ct).ConfigureAwait(false);
        claims.DisplayName = employee.FullName;
        claims.Roles.Add(employee.Role.RoleNameOf());
        foreach (var team in managed)
            claims.Roles.Add(TeamScopes.ManagerRoleOf(team.Id));
    }
}
