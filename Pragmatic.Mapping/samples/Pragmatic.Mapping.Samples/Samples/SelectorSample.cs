using Pragmatic.Mapping.Samples.Dtos;
using Pragmatic.Mapping.Samples.Entities;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates the use of the generated Selector property for in-memory mapping.
///     The Selector is a Func&lt;TSource, TDto&gt; delegate optimized for LINQ operations.
/// </summary>
public static class SelectorSample
{
    public static void Run()
    {
        Console.WriteLine("--- Selector (In-Memory Func<>) Sample ---");

        // Create test data
        var users = new List<User>
        {
            new()
            {
                Id = 1, Email = "john@example.com", FirstName = "John", LastName = "Doe",
                CreatedAt = DateTime.Now
            },
            new()
            {
                Id = 2, Email = "jane@example.com", FirstName = "Jane", LastName = "Smith",
                CreatedAt = DateTime.Now
            },
            new()
            {
                Id = 3, Email = "bob@example.com", FirstName = "Bob", LastName = "Wilson",
                CreatedAt = DateTime.Now
            }
        };

        // Method 1: Using extension method (simple, readable)
        var dtosList = users.ToUserDto().ToList();
        Console.WriteLine($"Extension method: Mapped {dtosList.Count} users");

        // Method 2: Using Selector with LINQ Select (flexible, composable)
        var dtosWithSelector = users
            .Where(u => u.Id > 1)
            .Select(UserDto.Selector)
            .ToList();
        Console.WriteLine($"Selector + Where: Mapped {dtosWithSelector.Count} users (filtered)");

        // Method 3: Selector for deferred execution
        var deferredQuery = users
            .AsEnumerable()
            .Where(u => u.Email.Contains("example"))
            .Select(UserDto.Selector);

        Console.WriteLine($"Deferred query count: {deferredQuery.Count()}");

        // Method 4: Using Selector as method group in other APIs
        var mappedArray = Array.ConvertAll(users.ToArray(), UserDto.Selector.Invoke);
        Console.WriteLine($"Array.ConvertAll: Mapped {mappedArray.Length} users");

        // Method 5: Passing Selector to methods that accept Func<>
        ProcessWithSelector(users, UserDto.Selector);

        Console.WriteLine();
    }

    private static void ProcessWithSelector<TSource, TDto>(
        IEnumerable<TSource> items,
        Func<TSource, TDto> selector)
    {
        var results = items.Select(selector).ToList();
        Console.WriteLine($"ProcessWithSelector: Processed {results.Count} items");
    }
}
