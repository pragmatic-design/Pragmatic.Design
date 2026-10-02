using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity;

namespace Pragmatic.Identity.Samples.Samples;

/// <summary>
///     L2 — Groups, Roles, Permissions chain + resource-level authorization.
///
///     Shows the full resolution pipeline User → Groups → Roles → Permissions
///     as it happens at runtime inside <c>CachedPermissionResolver</c>.
///     Wildcards (<c>booking.*</c>) are expanded by <c>WildcardMatcher</c>.
///     Resource-level checks run through <see cref="IResourceAuthorizer{TResource}"/>.
///
///     Uses <see cref="PragmaticBuilderAuthorizationExtensions.AddPragmaticAuthorization"/>
///     to wire the real DI graph — the resolver here is the same type a host
///     would receive. The only demo-only pieces are the in-memory
///     <see cref="ICurrentUser"/> (we don't have an HTTP pipeline) and the
///     call-site that resolves the resolver from DI per scenario.
/// </summary>
public static class GroupsAndRolesSample
{
    public static void Run()
    {
        Console.WriteLine("═══ L2 — Groups / Roles / Permissions + Resource authorization ═══");
        Console.WriteLine();

        // Shared registrations — the user changes per scenario via a mutable
        // holder so every scenario exercises a fresh CachedPermissionResolver
        // scope without rebuilding the whole provider.
        var services = new ServiceCollection();
        var userHolder = new MutableCurrentUserHolder();
        services.AddScoped<ICurrentUser>(_ => userHolder.Current);

        services.AddPragmaticAuthorization(authz => authz
            .MapRole("docs-reader",    r => r.WithPermissions("docs.read"))
            .MapRole("docs-editor",    r => r.WithPermissions("docs.read", "docs.write"))
            .MapRole("docs-owner",     r => r.WithPermissions("docs.*"))
            .MapRole("board-member",   r => r.WithPermissions("board.read", "board.meet"))
            .MapGroup("engineering",   g => g.WithRoles("docs-editor"))
            .MapGroup("executives",    g => g.WithRoles("docs-owner", "board-member"))
            .AddResourceAuthorizer<DocumentOwnerAuthorizer, Document>());

        using var provider = services.BuildServiceProvider();

        ScenarioRolesDirect(provider, userHolder);
        ScenarioGroupsChain(provider, userHolder);
        ScenarioWildcards(provider, userHolder);
        ScenarioResourceAuthorization(provider, userHolder);

        Console.WriteLine();
    }

    // ===== Scenario 1 — user with direct role claims ========================
    private static void ScenarioRolesDirect(IServiceProvider root, MutableCurrentUserHolder holder)
    {
        Console.WriteLine("  1) user with role claim 'docs-reader' (no groups)");
        holder.Current = new DemoUser(
            id: "user:alice",
            displayName: "Alice",
            claims: new() { ["role"] = ["docs-reader"] });

        using var scope = root.CreateScope();
        var authz = scope.ServiceProvider.GetRequiredService<IUserAuthorization>();

        Console.WriteLine($"     roles                 : [{string.Join(", ", authz.Roles)}]");
        Console.WriteLine($"     permissions           : [{string.Join(", ", authz.Permissions)}]");
        Console.WriteLine($"     HasPermission('docs.read')   : {authz.HasPermission("docs.read")}  (expects True)");
        Console.WriteLine($"     HasPermission('docs.write')  : {authz.HasPermission("docs.write")} (expects False — reader only)");
        Console.WriteLine();
    }

    // ===== Scenario 2 — user with group claim, chain expansion ==============
    private static void ScenarioGroupsChain(IServiceProvider root, MutableCurrentUserHolder holder)
    {
        Console.WriteLine("  2) user ∈ group 'engineering' → role 'docs-editor' → {docs.read, docs.write}");
        holder.Current = new DemoUser(
            id: "user:bob",
            displayName: "Bob",
            claims: new() { ["group"] = ["engineering"] });

        using var scope = root.CreateScope();
        var authz = scope.ServiceProvider.GetRequiredService<IUserAuthorization>();

        Console.WriteLine($"     groups                : [{string.Join(", ", authz.Groups)}]");
        Console.WriteLine($"     roles                 : [{string.Join(", ", authz.Roles)}] (none — resolved via group)");
        Console.WriteLine($"     permissions (chain)   : [{string.Join(", ", authz.Permissions)}]");
        Console.WriteLine($"     HasPermission('docs.write')  : {authz.HasPermission("docs.write")} (expects True — via group→role)");
        Console.WriteLine($"     HasPermission('board.read')  : {authz.HasPermission("board.read")} (expects False)");
        Console.WriteLine();
    }

    // ===== Scenario 3 — wildcard expansion ==================================
    private static void ScenarioWildcards(IServiceProvider root, MutableCurrentUserHolder holder)
    {
        Console.WriteLine("  3) user ∈ groups 'engineering'+'executives' → wildcard 'docs.*'");
        holder.Current = new DemoUser(
            id: "user:carol",
            displayName: "Carol",
            claims: new() { ["group"] = ["engineering", "executives"] });

        using var scope = root.CreateScope();
        var authz = scope.ServiceProvider.GetRequiredService<IUserAuthorization>();

        Console.WriteLine($"     groups                : [{string.Join(", ", authz.Groups)}]");
        Console.WriteLine($"     permissions           : [{string.Join(", ", authz.Permissions.OrderBy(p => p))}]");
        Console.WriteLine($"     HasPermission('docs.read')   : {authz.HasPermission("docs.read")}  (expects True — matched by 'docs.*')");
        Console.WriteLine($"     HasPermission('docs.delete') : {authz.HasPermission("docs.delete")}(expects True — matched by 'docs.*')");
        Console.WriteLine($"     HasPermission('board.read')  : {authz.HasPermission("board.read")} (expects True — via executives)");
        Console.WriteLine($"     HasPermission('payroll.read'): {authz.HasPermission("payroll.read")}(expects False — not covered)");
        Console.WriteLine();
    }

    // ===== Scenario 4 — resource-level (ABAC) ===============================
    private static void ScenarioResourceAuthorization(IServiceProvider root, MutableCurrentUserHolder holder)
    {
        Console.WriteLine("  4) resource authorization — policy: 'only the owner can edit'");
        holder.Current = new DemoUser(
            id: "user:bob",
            displayName: "Bob",
            claims: new() { ["group"] = ["engineering"] });

        using var scope = root.CreateScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<IResourceAuthorizer<Document>>();
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        var bobsDoc = new Document(Id: "doc:42", Title: "Release notes", OwnerId: "user:bob");
        var aliceDoc = new Document(Id: "doc:99", Title: "Secret plans",  OwnerId: "user:alice");

        var canEditOwn    = authorizer.CanAccessAsync(user, bobsDoc,  action: "edit").AsTask().GetAwaiter().GetResult();
        var canEditOthers = authorizer.CanAccessAsync(user, aliceDoc, action: "edit").AsTask().GetAwaiter().GetResult();

        Console.WriteLine($"     bob edits bob's doc   : {canEditOwn}  (expects True)");
        Console.WriteLine($"     bob edits alice's doc : {canEditOthers} (expects False — not owner)");
    }

    // ===== Demo-only pieces =================================================

    private sealed class MutableCurrentUserHolder
    {
        public ICurrentUser Current { get; set; } = AnonymousUser.Instance;
    }

    private sealed class DemoUser(
        string id,
        string? displayName,
        Dictionary<string, IReadOnlyList<string>> claims) : ICurrentUser
    {
        public string Id => id;
        public string? DisplayName => displayName;
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => claims;
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    private sealed record Document(string Id, string Title, string OwnerId);

    private sealed class DocumentOwnerAuthorizer : IResourceAuthorizer<Document>
    {
        public ValueTask<bool> CanAccessAsync(
            ICurrentUser user, Document resource, string action, CancellationToken ct = default)
        {
            if (action == "read") return ValueTask.FromResult(true);
            return ValueTask.FromResult(resource.OwnerId == user.Id);
        }
    }
}
