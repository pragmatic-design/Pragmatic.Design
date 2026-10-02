namespace Pragmatic.Ensure.Samples.Samples;

/// <summary>
///     Demonstrates the two-parameter tuple overload of <c>Ensure.ThrowIfNull</c>,
///     which validates two reference-type arguments at once and returns them as a
///     deconstructable tuple for fluent assignment in a constructor.
/// </summary>
public static class ThrowIfNullTupleSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. ThrowIfNull — Two-Parameter Tuple Overload");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowHappyPath();
        ShowFailingArgument();

        Console.WriteLine();
    }

    private static void ShowHappyPath()
    {
        Console.WriteLine("  6.1 Both arguments non-null — returns a (T1, T2) tuple");
        Console.WriteLine("  --------------------------------------------------------");

        IRepository repo = new InMemoryRepository();
        ILogger logger = new ConsoleLogger();

        // One call validates BOTH arguments and returns a deconstructable tuple.
        var service = new OrderProcessor(repo, logger);
        Console.WriteLine($"    OrderProcessor created — repo: {service.RepoName}, logger: {service.LoggerName}");

        // The tuple overload can also be deconstructed directly.
        var (r, l) = Ensure.ThrowIfNull(repo, logger);
        Console.WriteLine($"    Deconstructed tuple → ({r.GetType().Name}, {l.GetType().Name})");

        Console.WriteLine();
    }

    private static void ShowFailingArgument()
    {
        Console.WriteLine("  6.2 One argument null — throws naming the offending parameter");
        Console.WriteLine("  --------------------------------------------------------------");

        IRepository repo = new InMemoryRepository();

        try
        {
            // Second argument is null — CallerArgumentExpression captures its name.
            _ = new OrderProcessor(repo, null!);
        }
        catch (ArgumentNullException ex)
        {
            Console.WriteLine($"    new OrderProcessor(repo, null) → {ex.GetType().Name} (param: {ex.ParamName})");
        }

        Console.WriteLine();
    }

    private interface IRepository
    {
        string Name { get; }
    }

    private interface ILogger
    {
        string Name { get; }
    }

    private sealed class InMemoryRepository : IRepository
    {
        public string Name => "InMemory";
    }

    private sealed class ConsoleLogger : ILogger
    {
        public string Name => "Console";
    }

    /// <summary>
    ///     Realistic constructor: validate two dependencies in a single guard call.
    /// </summary>
    private sealed class OrderProcessor
    {
        private readonly IRepository _repository;
        private readonly ILogger _logger;

        public OrderProcessor(IRepository repository, ILogger logger)
        {
            // Tuple overload: validates both, returns both, assign via deconstruction.
            (_repository, _logger) = Ensure.ThrowIfNull(repository, logger);
        }

        public string RepoName => _repository.Name;
        public string LoggerName => _logger.Name;
    }
}
