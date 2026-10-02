namespace Pragmatic.Ensure.Samples.Samples;

/// <summary>
///     Demonstrates ThrowIf pattern for constructor parameter validation.
/// </summary>
public static class ConstructorGuardsSample
{
    public static void Run()
    {
        Console.WriteLine("--- Constructor Guards Sample ---\n");

        // Valid user creation
        var user = new User(
            Guid.NewGuid(),
            "John Doe",
            "john@example.com",
            25);

        Console.WriteLine($"Created user: {user.Name} ({user.Email}), Age: {user.Age}");

        // Try invalid cases
        TryInvalidCases();

        Console.WriteLine();
    }

    private static void TryInvalidCases()
    {
        // Null name (ThrowIfNullOrWhiteSpace throws ArgumentException)
        try
        {
            _ = new User(Guid.NewGuid(), null!, "test@example.com", 25);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Caught: {ex.GetType().Name} - {ex.ParamName}: name cannot be null");
        }

        // Empty GUID
        try
        {
            _ = new User(Guid.Empty, "John", "test@example.com", 25);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Caught: {ex.GetType().Name} - {ex.ParamName}: GUID cannot be empty");
        }

        // Invalid email
        try
        {
            _ = new User(Guid.NewGuid(), "John", "invalid-email", 25);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Caught: {ex.GetType().Name} - {ex.ParamName}: invalid email format");
        }

        // Negative age
        try
        {
            _ = new User(Guid.NewGuid(), "John", "test@example.com", -5);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.WriteLine($"Caught: {ex.GetType().Name} - {ex.ParamName}: age cannot be negative");
        }

        // Age out of range
        try
        {
            _ = new User(Guid.NewGuid(), "John", "test@example.com", 200);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.WriteLine($"Caught: {ex.GetType().Name} - {ex.ParamName}: age out of valid range");
        }
    }

    /// <summary>
    ///     Example domain entity with constructor guards.
    /// </summary>
    private class User
    {
        public User(Guid id, string name, string email, int age)
        {
            // Validate all parameters using ThrowIf pattern
            Ensure.ThrowIfEmpty(id);
            Ensure.ThrowIfNullOrWhiteSpace(name);
            Ensure.ThrowIfNotEmail(email);
            Ensure.ThrowIfNegative(age);
            Ensure.ThrowIfOutOfRange(age, 0, 150);

            Id = id;
            Name = name;
            Email = email;
            Age = age;
        }

        public Guid Id { get; }
        public string Name { get; }
        public string Email { get; }
        public int Age { get; }
    }
}