namespace Casework.Intake.Infrastructure.Authorization;

/// <summary>
///     Whoever runs the service rather than the cases: they add the organisations it serves.
/// </summary>
/// <remarks>
///     <para>
///         A second role, and not another grant on <see cref="CaseworkerRole" />: adding an
///         organisation is not a step of handling a case, and the two are held by different people. A
///         caseworker who could onboard could give themselves a tenant.
///     </para>
///     <para>
///         ⚠️ It grants <b>create</b> and nothing else. Reading the register back, changing a connection
///         string and deactivating an organisation are operations this example does not have; a role that
///         granted them now would be a permission nobody can exercise and nobody would notice acquiring.
///     </para>
/// </remarks>
[Role("service-operator", "Adds the organisations this service serves")]
[Grants(IntakePermissions.Organisation.Create)]
public sealed partial class ServiceOperatorRole;
