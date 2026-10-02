namespace Invoicing.Registry.Entities;

/// <summary>The rules an organization is read by, beside the generated <c>ById</c> and <c>BySlug</c>.</summary>
public static partial class OrganizationSpecifications
{
    /// <summary>
    ///     Every company the service knows, whatever its state — what the tenant store enumerates, and the
    ///     one read in this application that is deliberately not filtered by anything.
    /// </summary>
    public static Specification<Organization> All()
        => Spec<Organization>.Where(_ => true);
}
