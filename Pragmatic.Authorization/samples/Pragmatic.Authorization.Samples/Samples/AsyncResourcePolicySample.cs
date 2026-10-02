using Pragmatic.Authorization.Policy;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     Demonstrates <see cref="AsyncResourcePolicy"/> for authorization decisions that require I/O
///     (database lookups, remote permission services). Shows the <c>RequireExternalPermission</c>
///     factory, async composition with <c>&amp; | !</c>, and the implicit conversion that lets a
///     synchronous <see cref="ResourcePolicy"/> participate in an async policy tree.
/// </summary>
public static class AsyncResourcePolicySample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Async Resource Policy Sample ---");

        // In-memory fake of an external collaborator service: simulates an async lookup
        // ("is this user a shared collaborator?"). In production this would be an HTTP/DB call.
        var collaborators = new HashSet<string> { "user-456" };

        async ValueTask<bool> IsCollaboratorAsync(ICurrentUser user, CancellationToken ct)
        {
            await Task.Yield(); // stand-in for real async I/O
            return collaborators.Contains(user.Id);
        }

        // Async policy backed by the external check.
        var isCollaborator = AsyncResourcePolicy.RequireExternalPermission(IsCollaboratorAsync);

        // A synchronous policy is implicitly convertible to async, so it can be composed in.
        AsyncResourcePolicy isAuthenticated = ResourcePolicy.IsAuthenticated();
        AsyncResourcePolicy isOwner = ResourcePolicy.RequirePermission("documents.own");

        // Composed: authenticated AND (owner OR collaborator).
        var canEdit = isAuthenticated & (isOwner | isCollaborator);

        var owner = new SampleUser("user-123", "Owner", permissions: ["documents.own"]);
        var collaborator = new SampleUser("user-456", "Collaborator");
        var stranger = new SampleUser("user-999", "Stranger");
        var anonymous = AnonymousUser.Instance;

        Console.WriteLine($"Owner can edit:        {await canEdit.EvaluateAsync(owner)}");
        Console.WriteLine($"Collaborator can edit: {await canEdit.EvaluateAsync(collaborator)}");
        Console.WriteLine($"Stranger can edit:     {await canEdit.EvaluateAsync(stranger)}");
        Console.WriteLine($"Anonymous can edit:    {await canEdit.EvaluateAsync(anonymous)}");

        // Negation: passes for everyone EXCEPT collaborators.
        var notCollaborator = !isCollaborator;
        Console.WriteLine($"Stranger is NOT collaborator:     {await notCollaborator.EvaluateAsync(stranger)}");
        Console.WriteLine($"Collaborator is NOT collaborator: {await notCollaborator.EvaluateAsync(collaborator)}");

        Console.WriteLine("Async resource policy evaluation complete.");
        Console.WriteLine();
    }
}
