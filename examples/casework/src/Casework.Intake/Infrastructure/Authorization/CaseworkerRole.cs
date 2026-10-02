namespace Casework.Intake.Infrastructure.Authorization;

/// <summary>
///     Whoever works the cases: opens them, corrects what they say, reads them back.
/// </summary>
/// <remarks>
///     <para>
///         One role, because the example has one job to do. Deciding a case is not in it and is not in
///         any other role either: a decision is a transition, and a transition is asked for by name —
///         <c>intake.case.update</c> does not carry it. A role that granted
///         <c>IntakePermissions.Case.All</c> would quietly acquire every permission added after it,
///         which is how a wildcard becomes a privilege nobody granted.
///     </para>
///     <para>
///         The permissions are the generated constants and not strings: the boundary emits them from the
///         entity (<c>_Infra.Identity.EntityPermissions.g.cs</c>), so renaming the entity is a compile
///         error here instead of a 403 in production.
///     </para>
/// </remarks>
[Role("caseworker", "Opens and keeps the cases")]
[Grants(
    IntakePermissions.Case.Read,
    IntakePermissions.Case.Create,
    IntakePermissions.Case.Update,
    // The letter an applicant receives is the office's own, and changing its wording is
    // part of working the cases rather than of running the service. It is the same people who answer
    // for what it says.
    IntakePermissions.LetterTemplate.Create)]
public sealed partial class CaseworkerRole;
