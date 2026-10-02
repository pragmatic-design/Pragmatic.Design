using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Identity.Diagnostics;

/// <summary>
///     Diagnostic descriptors for the Identity/Authorization SG feature.
///     Range: PRAG1000-1099.
/// </summary>
internal static class IdentityDiagnostics
{
    // NOTE: PRAG1000 (EmptyPermissionName) reported an IPermission whose Name could not be read. IPermission
    // is gone — a permission is an [assembly: Permission] line, whose empty value is PRAG1004 —
    // so the descriptor went with it. Do not reuse the ID.

    public static readonly DiagnosticDescriptor DuplicatePermissionName = DiagnosticFactory.Error(
        "PRAG1001", "Duplicate permission name",
        "Permission name '{0}' is defined by both '{1}' and '{2}'",
        "Each permission name must be unique across the assembly.");

    // NOTE: PRAG1002 (PermissionNameConvention) was declared here but never reported — no naming
    // convention check exists. Removed: do not reuse the ID for anything else.

    /// <summary>
    ///     A declared permission whose first segment is none of the assembly's boundaries: its constant has no
    ///     <c>{Boundary}Permissions</c> class to go into.
    /// </summary>
    public static readonly DiagnosticDescriptor PermissionOutsideTheBoundaries = DiagnosticFactory.Error(
        "PRAG1004", "A declared permission names no boundary of this assembly",
        "Permission '{0}', declared by {1}, names no boundary of this assembly — its first segment must be one of: {2}",
        "A custom permission is {boundary}.{resource}.{verb}, and its constant goes into that boundary's permissions class, beside the CRUD permissions of its entities.");

    /// <summary>
    ///     A declared permission whose constant would take a name its class already gives to something else — a
    ///     nested class, a constant, the class itself: CS0102 or CS0542 in a generated file.
    /// </summary>
    public static readonly DiagnosticDescriptor PermissionConstantNameTaken = DiagnosticFactory.Error(
        "PRAG1005", "A declared permission's constant would take a name already in use",
        "Permission '{0}', declared by {1}, needs the name '{2}', which {3} already uses",
        "A permission's constant is named after its segments: a resource is a nested class, the verb a constant. Pick a value whose segments do not reuse, at the same depth, the name of a resource, of an entity or of another permission's verb.");

    /// <summary><c>[Role]</c> on a class the generator cannot write the <c>IRole</c> members into.</summary>
    public static readonly DiagnosticDescriptor RoleMustBePartial = DiagnosticFactory.Error(
        "PRAG1006", "A [Role] class must be a top-level partial class",
        "'{0}' declares [Role] but is not a top-level partial class, so its IRole members cannot be generated — it is no role at all",
        "Declare the class 'partial', outside any other type: the generator writes Name, Description and DefaultPermissions into it.");

    /// <summary>Roles that include each other: there is no set of permissions to flatten.</summary>
    public static readonly DiagnosticDescriptor RoleInclusionCycle = DiagnosticFactory.Error(
        "PRAG1007", "Roles include each other",
        "'{0}' includes itself through {1}",
        "A role includes the permissions of the roles it names; remove one [IncludesRole<T>] from the loop.");

    /// <summary>A granted constant no generator of this compilation writes.</summary>
    public static readonly DiagnosticDescriptor GrantedPermissionNotResolved = DiagnosticFactory.Error(
        "PRAG1008", "A granted permission does not resolve",
        "'{0}' grants '{1}', which no generator of this compilation writes — the role would be catalogued granting less than it was declared to",
        "Name a generated constant (an entity's CRUD, or an [assembly: Permission]), a constant of a referenced assembly, or write the permission's value.");

    /// <summary>An included hand-written role of another assembly, whose permissions are a property body.</summary>
    public static readonly DiagnosticDescriptor IncludedRoleCannotBeRead = DiagnosticFactory.Error(
        "PRAG1009", "An included role's permissions cannot be read",
        "'{0}' includes '{1}', a hand-written role of another assembly — its permissions are a property body the generator cannot read",
        "Declare that role with [Role] and [Grants] in its assembly: its attributes are in the metadata, and the generator reads them.");

    /// <summary><c>roles.pragmatic.json</c> that does not parse: none of its roles exists at runtime.</summary>
    public static readonly DiagnosticDescriptor RoleSeedingFileNotJson = DiagnosticFactory.Error(
        "PRAG1010", "roles.pragmatic.json is not valid JSON",
        "'{0}' is not valid JSON ({1}) — none of the roles and groups it declares is seeded",
        "Fix the JSON: SeedFromJson() is generated from the file only when it parses.");

    /// <summary>A <c>roles.pragmatic.json</c> the parser could read but not understand.</summary>
    public static readonly DiagnosticDescriptor RoleSeedingFileInvalid = DiagnosticFactory.Error(
        "PRAG1011", "roles.pragmatic.json declares something it cannot",
        "'{0}': {1}",
        "A role has 'description', 'permissions' (strings) and 'inherits' (names of roles in the file); a group has 'description' and 'roles'.");

    /// <summary>A second <c>roles.pragmatic.json</c>: only one is read.</summary>
    public static readonly DiagnosticDescriptor RoleSeedingFileIgnored = DiagnosticFactory.Error(
        "PRAG1012", "Only one roles.pragmatic.json is read",
        "'{0}' is not read — the roles are seeded from '{1}', and the roles and groups this one declares would not exist",
        "Keep one roles.pragmatic.json per project, or remove the others from AdditionalFiles.");

    /// <summary>
    ///     A spread in a hand-written role's list the catalogue cannot follow: the runtime grants what it names,
    ///     the role registry does not list it.
    /// </summary>
    public static readonly DiagnosticDescriptor RoleSpreadCannotBeRead = DiagnosticFactory.Warning(
        "PRAG1013", "A spread in a role's permissions cannot be read",
        "'{0}' spreads '{1}' into DefaultPermissions, which the generator cannot read — the role grants those permissions at runtime, and the role registry does not list them",
        "Spread another role's DefaultPermissions, or a list held in a field or property of this compilation; or declare the role with [Role], [IncludesRole<T>] and [Grants].");

    /// <summary>A member of a <c>[SignsInAs]</c> enum that signs in as no role.</summary>
    /// <remarks>
    ///     An error: the mapping is generated only when every member has its arm, so a value the enum declares can
    ///     never reach a sign-in with no role — the hand-written switch it replaces threw at runtime instead.
    /// </remarks>
    public static readonly DiagnosticDescriptor SignInMemberWithoutRole = DiagnosticFactory.Error(
        "PRAG1014", "An access level signs in as no role",
        "'{0}.{1}' has no [SignsInAs<TRole>] while other members of '{0}' do — a user with it would sign in with no role",
        "Declare the role it signs in as on the member: [SignsInAs<EmployeeRole>].");

    /// <summary>
    ///     A role's whole list lives in another assembly that does not publish it: the catalogue cannot say what
    ///     the role grants, and must not say "nothing".
    /// </summary>
    /// <remarks>
    ///     A warning, not an error: the role is real and the runtime grants what the list holds — it is the
    ///     catalogue that cannot tell, and the two fixes it names are both one move.
    /// </remarks>
    public static readonly DiagnosticDescriptor RoleListCannotBeRead = DiagnosticFactory.Warning(
        "PRAG1015", "A role's permissions live in a list this compilation cannot read",
        "'{0}' reads its DefaultPermissions from '{1}', which lives in another assembly and is not published — the role grants those permissions at runtime, and the role registry lists none of them",
        "Mark the list with [PermissionSet] in the assembly that declares it, so its values travel in metadata; or move the list into this assembly, or write the permissions here.");

    /// <summary>A <c>[PermissionSet]</c> list the generator cannot read, so this assembly publishes nothing.</summary>
    /// <remarks>
    ///     The other half of <see cref="RoleListCannotBeRead" />: without it an author marks the list, nothing
    ///     is published, and the only complaint appears in the <em>other</em> assembly, about the role.
    /// </remarks>
    public static readonly DiagnosticDescriptor PermissionSetCannotBeRead = DiagnosticFactory.Warning(
        "PRAG1016", "A [PermissionSet] list cannot be read",
        "'{0}' is marked [PermissionSet] and the generator cannot read its values, so this assembly publishes nothing for it",
        "Write the list as a collection expression or an array initializer, in place or in a member this compilation declares — the shapes a role's DefaultPermissions accepts.");

    public static readonly DiagnosticDescriptor EmptyRoleName = DiagnosticFactory.Error(
        "PRAG1003", "IRole.Name must be non-empty",
        "Type '{0}' implements IRole but Name is empty or could not be resolved at compile time",
        "Ensure the static abstract Name property returns a non-empty string constant (a literal or a const).");
}
