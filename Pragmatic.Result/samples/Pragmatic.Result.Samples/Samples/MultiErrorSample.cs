// =============================================================================
// Multiple error types with generated variants
// =============================================================================

using Pragmatic.Result.Http;
using Pragmatic.Result.Samples.Errors;

namespace Pragmatic.Result.Samples.Samples;

public static class MultiErrorSample
{
    public static void Run()
    {
        Console.WriteLine("3. Multiple Error Types");
        Console.WriteLine("-----------------------");

        var user1 = GetUser("123");
        var user2 = GetUser("");
        var user3 = GetUser("unknown");

        Console.WriteLine(
            $"User 123: {user1.Match(u => u.Name, e => e.Message, e => $"{e.EntityType} {e.EntityId} not found")}");
        Console.WriteLine(
            $"Empty ID: {user2.Match(u => u.Name, e => e.Message, e => $"{e.EntityType} {e.EntityId} not found")}");
        Console.WriteLine(
            $"Unknown: {user3.Match(u => u.Name, e => e.Message, e => $"{e.EntityType} {e.EntityId} not found")}");
        Console.WriteLine();
    }

    private static Result<User, ValidationError, NotFoundError> GetUser(string id)
    {
        if (string.IsNullOrEmpty(id))
            return new ValidationError("ID cannot be empty");
        if (id == "unknown")
            return NotFoundError.Create("User", id);
        return new User(id, "John Doe");
    }
}

public record User(string Id, string Name);