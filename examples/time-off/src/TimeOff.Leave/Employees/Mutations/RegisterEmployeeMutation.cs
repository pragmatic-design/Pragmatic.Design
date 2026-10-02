using System.Security.Cryptography;
using Pragmatic.Authoring;
using TimeOff.Leave.Events;

namespace TimeOff.Leave.Employees.Mutations;

/// <summary>
///     HR registers an employee: who they are, when they started, their team and what they may do. The
///     employee gets a number and an account, and is invited to choose a password.
/// </summary>
/// <remarks>
///     <para>
///         The account is opened with a password nobody knows — random, and only its hash kept — so it
///         exists, and can be found by the invitation, without anyone ever having had it. The employee
///         sets their own through the invitation, which is a password reset.
///     </para>
///     <para>
///         A work email that is already taken is refused by the unique index on it, and answered as
///         the conflict it is.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(LeavePermissions.Employee.Create)]
[Endpoint(HttpVerb.Post, "api/employees")]
[CreatedAt("/api/employees/{Id}")]
[ReturnsDto<EmployeeDto>]
[Raises<EmployeeRegistered>]
public partial class RegisterEmployeeMutation : Mutation<Employee>
{
    private IPasswordHasher _hasher = null!;

    public required string FullName { get; init; }

    public required string WorkEmail { get; init; }

    public required DateOnly HiredOn { get; init; }

    public Guid? TeamId { get; init; }

    public AccessRole Role { get; init; } = AccessRole.Employee;

    public override Task<Result<Employee, IError>> ApplyAsync(Employee entity, CancellationToken ct = default)
    {
        entity.SetWorkEmail(WorkEmail.Trim().ToLowerInvariant());
        entity.OpenAccount(_hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

        return Task.FromResult(Result<Employee, IError>.Success(entity));
    }
}
