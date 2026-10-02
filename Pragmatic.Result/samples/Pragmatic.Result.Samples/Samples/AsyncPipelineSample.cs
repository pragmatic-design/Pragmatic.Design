// =============================================================================
// Async Pipeline Pattern
// =============================================================================

using Pragmatic.Result.Extensions;
using Pragmatic.Result.Http;

namespace Pragmatic.Result.Samples.Samples;

/// <summary>
///     Async railway using the real Task&lt;Result&gt; extensions from
///     <c>Pragmatic.Result.Extensions</c> — MapAsync / TapAsync / OnFailureAsync / OrElseAsync.
///     The async side-effect delegates take <c>Func&lt;T, Task&gt;</c>, so a synchronous
///     Console.WriteLine is wrapped with Task.CompletedTask.
/// </summary>
public static class AsyncPipelineSample
{
    public static void Run()
    {
        Console.WriteLine("8. Async Pipeline Pattern");
        Console.WriteLine("--------------------------");

        // Run the async demo synchronously for sample purposes
        RunAsyncDemo().GetAwaiter().GetResult();

        Console.WriteLine();
    }

    private static async Task RunAsyncDemo()
    {
        // Async pipeline with MapAsync + TapAsync (real ResultAsyncExtensions)
        var result = await GetUserAsync(1)
            .MapAsync(u => new { u.Id, u.Name, u.Email })
            .TapAsync(u =>
            {
                Console.WriteLine($"  Processing user: {u.Name}");
                return Task.CompletedTask;
            });

        Console.WriteLine($"  Pipeline result: {result.Match(u => $"User {u.Id}: {u.Email}", e => $"Error: {e.Code}")}");

        // Async pipeline with failure — OnFailureAsync observes the error without changing the result
        var failedResult = await GetUserAsync(999)
            .MapAsync(u => u.Name)
            .OnFailureAsync(e =>
            {
                Console.WriteLine($"  Pipeline failed with: {e.Code}");
                return Task.CompletedTask;
            });

        Console.WriteLine($"  Failed pipeline: {failedResult.Match(n => n, e => $"Not found: {e.EntityType}")}");

        // Async pipeline with recovery — OrElseAsync supplies a fallback Result
        var recoveredResult = await GetUserAsync(999)
            .OrElseAsync(async _ =>
            {
                await Task.Delay(10); // Simulate async recovery
                return Result<User, NotFoundError>.Success(new User(0, "Guest", "guest@example.com"));
            });

        Console.WriteLine($"  Recovered result: {recoveredResult.Match(u => u.Name, _ => "Still failed")}");
    }

    // Simulated async repository
    private static async Task<Result<User, NotFoundError>> GetUserAsync(int id)
    {
        await Task.Delay(10); // Simulate async operation

        if (id == 1)
            return new User(1, "John Doe", "john@example.com");

        return NotFoundError.Create("User", id);
    }

    private record User(int Id, string Name, string Email);
}
