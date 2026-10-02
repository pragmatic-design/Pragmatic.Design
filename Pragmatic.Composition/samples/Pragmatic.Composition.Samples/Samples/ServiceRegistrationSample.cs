using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Extensions;

namespace Pragmatic.Composition.Samples.Samples;

/// <summary>
///     DI registration attributes processed by the source generator at compile-time:
///     <c>[Service]</c>, <c>[Service&lt;TInterface&gt;]</c> (incl. keyed),
///     <c>[ServiceFactory]</c>/<c>[Factory]</c>, <c>[Inject]</c>, and <c>[Decorator]</c>.
///     The attribute usage is shown as it appears in source; the decorator chain and keyed
///     resolution are demonstrated live against a real container.
/// </summary>
public static class ServiceRegistrationSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. Service Registration Attributes — [Service]/[Factory]/[Inject]/[Decorator]");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  [Service] — auto-register (SG emits the AddScoped/Singleton/...):");
        Console.WriteLine("  ────────────────────────────────────────────────────────────────");
        Console.WriteLine("""
            [Service]                                          // Scoped, first interface
            public class OrderService : IOrderService { }

            [Service(Lifetime = Lifetime.Singleton, AsSelf = true)]
            public class Cache { }                             // registered as concrete type

            [Service<IPaymentGateway>(Lifetime = Lifetime.Singleton, Key = "stripe")]
            public class StripeGateway : IPaymentGateway { }   // keyed singleton
        """);
        Console.WriteLine();

        Console.WriteLine("  [ServiceFactory] / [Factory] — factory methods (return type = service):");
        Console.WriteLine("  ──────────────────────────────────────────────────────────────────────");
        Console.WriteLine("""
            [ServiceFactory]                                   // factory class is a singleton
            public class ConnectionFactory
            {
                [Factory(Lifetime = Lifetime.Scoped)]
                public IDbConnection CreateConnection(IConfiguration cfg)
                    => new SqlConnection(cfg.GetConnectionString("App"));
            }
        """);
        Console.WriteLine();

        Console.WriteLine("  [Inject] — property / method injection after construction:");
        Console.WriteLine("  ───────────────────────────────────────────────────────────");
        Console.WriteLine("""
            [Service]
            public partial class ReportService
            {
                [Inject] public ILogger<ReportService> Logger { get; set; }       // optional
                [Inject(Required = true)] public IClock Clock { get; set; }        // fail-fast
                [Inject(Key = "stripe")] public IPaymentGateway Gateway { get; set; }
            }
        """);
        Console.WriteLine();

        // Keyed registration is a plain DI feature the SG emits — demonstrate it live.
        var keyed = new ServiceCollection();
        keyed.AddKeyedSingleton<IPaymentGateway, StripeGateway>("stripe");
        keyed.AddKeyedSingleton<IPaymentGateway, PaypalGateway>("paypal");
        using var keyedProvider = keyed.BuildServiceProvider();
        var stripe = keyedProvider.GetRequiredKeyedService<IPaymentGateway>("stripe");
        var paypal = keyedProvider.GetRequiredKeyedService<IPaymentGateway>("paypal");
        Console.WriteLine("  Keyed resolution (live, what [Service<T>(Key=...)] produces):");
        Console.WriteLine($"    key 'stripe' -> {stripe.Charge(10):C0}-equivalent via {stripe.GetType().Name}");
        Console.WriteLine($"    key 'paypal' -> via {paypal.GetType().Name}");
        Console.WriteLine();

        Console.WriteLine("  [Decorator] — wrap a service, applied in ascending Order (live):");
        Console.WriteLine("  ─────────────────────────────────────────────────────────────────");
        var decorated = new ServiceCollection();
        decorated.AddSingleton<IGreeter, PlainGreeter>();
        // [Decorator(Order = 0)] then [Decorator(Order = 1)] => SG emits these Decorate() calls in order.
        decorated.Decorate<IGreeter, ShoutingGreeter>();   // Order 0: closest to original
        decorated.Decorate<IGreeter, BracketGreeter>();    // Order 1: outermost
        using var decoratedProvider = decorated.BuildServiceProvider();
        var greeter = decoratedProvider.GetRequiredService<IGreeter>();
        Console.WriteLine("""
            [Service] class PlainGreeter : IGreeter { ... }
            [Decorator(Order = 0)] class ShoutingGreeter(IGreeter inner) : IGreeter { ... }
            [Decorator(Order = 1)] class BracketGreeter(IGreeter inner) : IGreeter { ... }
        """);
        Console.WriteLine($"    greeter.Greet(\"world\") => {greeter.Greet("world")}");
        Console.WriteLine("    (BracketGreeter wraps ShoutingGreeter wraps PlainGreeter)");
        Console.WriteLine();
    }
}

/// <summary>Demo service contract for keyed registration.</summary>
public interface IPaymentGateway
{
    decimal Charge(decimal amount);
}

/// <summary>Keyed implementation registered under "stripe".</summary>
public sealed class StripeGateway : IPaymentGateway
{
    public decimal Charge(decimal amount) => amount;
}

/// <summary>Keyed implementation registered under "paypal".</summary>
public sealed class PaypalGateway : IPaymentGateway
{
    public decimal Charge(decimal amount) => amount;
}

/// <summary>Demo service contract for the decorator chain.</summary>
public interface IGreeter
{
    string Greet(string name);
}

/// <summary>Original implementation wrapped by the decorator chain.</summary>
public sealed class PlainGreeter : IGreeter
{
    public string Greet(string name) => $"hello {name}";
}

/// <summary>Innermost decorator (lowest Order).</summary>
public sealed class ShoutingGreeter(IGreeter inner) : IGreeter
{
    public string Greet(string name) => inner.Greet(name).ToUpperInvariant();
}

/// <summary>Outermost decorator (highest Order).</summary>
public sealed class BracketGreeter(IGreeter inner) : IGreeter
{
    public string Greet(string name) => $"[{inner.Greet(name)}]";
}
