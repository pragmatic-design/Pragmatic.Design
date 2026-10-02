using Casework.Intake.Enums;
using Casework.Intake.Events;
using Pragmatic.Messaging;
using Pragmatic.MultiTenancy;

namespace Casework.Intake.Organisations.Actions;

/// <summary>
///     Registers an organisation, makes this service's database for it, and tells the other service to
///     make its own.
/// </summary>
/// <remarks>
///     <para>
///         <b>An action and not a mutation, because the register is not written through the DbContext.</b>
///         An organisation is the row a tenant is looked up in, and it lives on the shared database — see
///         <c>TheOrganisationsAreTheTenants</c>. A mutation would write it through the tenant-routed
///         context, which for an operator whose own organisation has a dedicated database means writing
///         the register into that database, where nothing reads it.
///     </para>
///     <para>
///         ⚠️ <b>This process is not a saga, and that is a decision.</b> It has a half-way state
///         — this service ready, the other not — so something must hold it. That something is the
///         organisation's own state: <c>Provisioning</c> until <c>TenantReady</c> comes back,
///         <c>Active</c> after. The framework already reads it on every request
///         (<c>EnforceTenantState</c> refuses a tenant that is not <c>Active</c> with 403), so a saga
///         would be a second copy of a fact the request pipeline consults elsewhere — and when the two
///         disagreed, the register would win while the saga looked correct.
///     </para>
///     <para>
///         ⚠️ <b>Nothing here is transactional and it must not pretend to be.</b> The register is SQL on
///         another connection, the database is DDL on a server, and the message is a publish: no
///         transaction spans them. What makes that safe is the order — the row exists before the work
///         starts, and the organisation cannot be used until the far end answers — and the fact that
///         every step is repeatable. A failure leaves a <c>Provisioning</c> row, which is a thing an
///         operator can see and retry, rather than a half-usable organisation.
///     </para>
///     <para>
///         The answer is 200 with the state in it, and not 202: an endpoint cannot declare a success
///         status of its own in this framework, and the honest thing to return was the state rather than
///         a status code that says "accepted" by convention.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(IntakePermissions.Organisation.Create)]
[Endpoint(HttpVerb.Post, "api/organisations")]
public partial class OnboardAnOrganisationAction : DomainAction<OrganisationDto, IError>
{
    private ITenantStore _organisations = null!;
    private IProvisionTenantDatabases _databases = null!;
    private IMessageBus _bus = null!;

    /// <summary>The id its people's tokens will carry. Letters, digits and underscores only.</summary>
    /// <remarks>
    ///     ⚠️ It ends up in a database name, so what may be in it is not a matter of taste: the host
    ///     builds the connection string with <c>TenantDatabaseOptions.BuildConnectionString</c>, which
    ///     refuses anything but letters, digits, <c>-</c> and <c>_</c> before formatting the template —
    ///     the check that keeps a claim out of a connection string.
    /// </remarks>
    [Required]
    [MaxLength(60)]
    public required string TenantKey { get; init; }

    [Required]
    [MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>
    ///     Whether it gets a database of its own here, or shares the schema with the small ones.
    /// </summary>
    /// <remarks>
    ///     It says what the <b>organisation</b> asked for, not what either service will do about it: this
    ///     service builds its own connection string from its own template, and the other one does the
    ///     same with its. Neither knows the other's.
    /// </remarks>
    public bool DedicatedDatabase { get; init; }

    /// <summary>When it was registered: the application's clock, never the caller's.</summary>
    [FromClock]
    public DateTimeOffset OnboardedOn { get; private set; }

    public override async Task<Result<OrganisationDto, IError>> Execute(CancellationToken ct = default)
    {
        if (await _organisations.GetByIdAsync(TenantKey, ct).ConfigureAwait(false) is not null)
            return Result<OrganisationDto, IError>.Failure(
                ConflictError.AlreadyExists(nameof(Organisation), TenantKey));

        // Where its rows will be, decided before anything exists. Null for an organisation that stays on
        // the shared schema: its rows go where everybody else's do, separated by the tenant column.
        var connectionString = _databases.ConnectionStringFor(TenantKey, DedicatedDatabase);

        // The row first, in Provisioning: it is what refuses requests as this organisation until the far
        // end answers, and what an operator finds if the far end never does — or if the line below
        // throws.
        await _organisations.CreateAsync(
                new TenantInfo
                {
                    TenantId = TenantKey,
                    TenantName = Name,
                    ConnectionString = connectionString,
                    State = TenantState.Provisioning,
                    CreatedAt = OnboardedOn
                },
                ct)
            .ConfigureAwait(false);

        // Then this service's own half: the database, and this service's schema in it.
        if (connectionString is not null)
            await _databases.ProvisionAsync(TenantKey, connectionString, ct).ConfigureAwait(false);

        // And the other service's half, which this one does not do and does not wait for.
        await _bus.PublishAsync(
                new TenantOnboarded(TenantKey, Name, DedicatedDatabase, OnboardedOn), ct)
            .ConfigureAwait(false);

        return new OrganisationDto(TenantKey, Name, nameof(OrganisationState.Provisioning));
    }
}
