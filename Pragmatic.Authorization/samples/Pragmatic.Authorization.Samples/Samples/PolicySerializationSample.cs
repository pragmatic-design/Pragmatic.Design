using System.Text.Json;
using Pragmatic.Authorization.Policy;
using Pragmatic.Authorization.Serialization;

namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     Demonstrates <see cref="PolicySerializer"/>: converting a composed
///     <see cref="ResourcePolicy"/> into a JSON-safe <see cref="PolicyExpression"/> tree, serializing
///     that tree to JSON for portable storage (database, configuration), then deserializing all the
///     way back to a live policy and evaluating it — proving the round-trip preserves behaviour.
/// </summary>
public static class PolicySerializationSample
{
    public static void Run()
    {
        Console.WriteLine("--- Policy Serialization Sample ---");

        // Build a composite policy: (read AND in "engineering" group) OR admin role.
        var policy =
            (ResourcePolicy.RequirePermission("documents.read") & ResourcePolicy.InGroup("engineering"))
            | ResourcePolicy.InRole("admin");

        // 1. ResourcePolicy -> PolicyExpression (JSON-safe DTO tree).
        PolicyExpression expression = PolicySerializer.Serialize(policy);

        // 2. PolicyExpression -> JSON string (e.g., to store in a DB or config column).
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(expression, options);
        Console.WriteLine("Serialized policy JSON:");
        Console.WriteLine(json);

        // 3. JSON -> PolicyExpression -> ResourcePolicy (full rehydration).
        var restoredExpression = JsonSerializer.Deserialize<PolicyExpression>(json)!;
        ResourcePolicy restored = PolicySerializer.Deserialize(restoredExpression);

        // 4. Evaluate original vs. round-tripped against the same users to prove equivalence.
        var engineer = new SampleUser("user-1", "Engineer", permissions: ["documents.read"], groups: ["engineering"]);
        var admin = new SampleUser("user-2", "Admin", roles: ["admin"]);
        var outsider = new SampleUser("user-3", "Outsider", permissions: ["documents.read"]);

        foreach (var (label, user) in new[] { ("Engineer", engineer), ("Admin", admin), ("Outsider", outsider) })
        {
            var original = policy.Evaluate(user);
            var roundTripped = restored.Evaluate(user);
            Console.WriteLine($"{label,-9} original={original,-5} restored={roundTripped,-5} match={original == roundTripped}");
        }

        Console.WriteLine("Policy serialization round-trip complete.");
        Console.WriteLine();
    }
}
