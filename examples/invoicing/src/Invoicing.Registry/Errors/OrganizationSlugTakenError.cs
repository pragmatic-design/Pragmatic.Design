namespace Invoicing.Registry.Errors;

/// <summary>
///     The slug is the tenant id, so two companies cannot share one — and the second one is told, rather
///     than meeting a unique-index violation from the database.
/// </summary>
public sealed partial record OrganizationSlugTakenError : Error
{
    public override string Code => "ORGANIZATION_SLUG_TAKEN";
    public override int StatusCode => 409;
}
