using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Extensions;
using Pragmatic.Composition.Scanning;

namespace Pragmatic.Composition.Samples.Samples;

/// <summary>
///     Assembly scanning fluent API (the <c>Scan()</c> extension on <c>IServiceCollection</c>):
///     source selection → filtering → registration shape → lifetime.
///     This sample is fully runnable: it scans this assembly into a real container.
/// </summary>
public static class AssemblyScanningSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. Assembly Scanning — Scan() Fluent API (runnable)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var services = new ServiceCollection();

        // Source → filter → registration shape → lifetime, all in one fluent chain.
        services.Scan(scan => scan
            .FromAssemblyOf<EmailNotifier>()
            .AddClasses(filter => filter.AssignableTo<INotifier>())
            .AsImplementedInterfaces()
            .WithSingletonLifetime());

        using var provider = services.BuildServiceProvider();

        // Every INotifier implementation in this assembly was discovered and registered.
        var notifiers = provider.GetServices<INotifier>().ToList();

        Console.WriteLine("  Scan(scan => scan");
        Console.WriteLine("      .FromAssemblyOf<EmailNotifier>()");
        Console.WriteLine("      .AddClasses(f => f.AssignableTo<INotifier>())");
        Console.WriteLine("      .AsImplementedInterfaces()");
        Console.WriteLine("      .WithSingletonLifetime());");
        Console.WriteLine();
        Console.WriteLine($"  Discovered {notifiers.Count} INotifier implementation(s):");
        foreach (var notifier in notifiers.OrderBy(n => n.GetType().Name))
            Console.WriteLine($"    - {notifier.GetType().Name}: {notifier.Notify("ping")}");
        Console.WriteLine();

        Console.WriteLine("  Source selection methods:");
        Console.WriteLine("  ─────────────────────────");
        Console.WriteLine("    FromAssemblyOf<T>()         — assembly that declares T");
        Console.WriteLine("    FromAssemblies(params[])    — explicit assemblies");
        Console.WriteLine("    FromCallingAssembly()       — the caller's assembly");
        Console.WriteLine("    FromEntryAssembly()         — the process entry assembly");
        Console.WriteLine("    FromAssembliesMatching(\"X.*\") — wildcard load (broad patterns rejected)");
        Console.WriteLine("    FromDependencyContext(\"X\")  — runtime deps by prefix");
        Console.WriteLine();

        Console.WriteLine("  Filters (ITypeFilter): AssignableTo<T>, WithAttribute<T>,");
        Console.WriteLine("    InNamespace/InNamespaceOf<T>, Where/NotWhere.");
        Console.WriteLine("  Shapes (ITypeSelector): AsImplementedInterfaces, AsSelf,");
        Console.WriteLine("    AsSelfWithInterfaces, As<T>, AsMatchingInterface.");
        Console.WriteLine("  Lifetimes: WithSingleton/Scoped/Transient/WithLifetime.");
        Console.WriteLine("  Duplicates: UsingRegistrationStrategy(Append|Skip|Replace|Throw).");
        Console.WriteLine();

        // Demonstrate the duplicate-registration strategy on a second container.
        var skipServices = new ServiceCollection();
        skipServices.AddSingleton<INotifier, EmailNotifier>();
        skipServices.Scan(scan => scan
            .FromAssemblyOf<EmailNotifier>()
            .AddClasses(filter => filter.AssignableTo<INotifier>())
            .AsImplementedInterfaces()
            .UsingRegistrationStrategy(RegistrationStrategy.Skip)
            .WithSingletonLifetime());

        using var skipProvider = skipServices.BuildServiceProvider();
        var afterSkip = skipProvider.GetServices<INotifier>().Count();
        Console.WriteLine($"  RegistrationStrategy.Skip kept the pre-existing registration:");
        Console.WriteLine($"    INotifier registrations after re-scan = {afterSkip} (no duplicates appended)");
        Console.WriteLine();
    }
}

/// <summary>Demo contract discovered by the scanner.</summary>
public interface INotifier
{
    string Notify(string message);
}

/// <summary>Demo implementation discovered by AssignableTo&lt;INotifier&gt;.</summary>
public sealed class EmailNotifier : INotifier
{
    public string Notify(string message) => $"email<{message}>";
}

/// <summary>Demo implementation discovered by AssignableTo&lt;INotifier&gt;.</summary>
public sealed class SmsNotifier : INotifier
{
    public string Notify(string message) => $"sms<{message}>";
}
