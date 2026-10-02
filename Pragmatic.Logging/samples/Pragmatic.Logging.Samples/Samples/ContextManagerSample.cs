using Pragmatic.Logging.Context;
using Pragmatic.Logging.Context.Providers;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     The <see cref="IContextManager"/> aggregates ambient properties from registered
///     <see cref="IContextProvider"/>s (machine, process, thread, custom). <see cref="LogContextScope"/>
///     layers per-operation properties on top using <c>AsyncLocal</c> so they flow across
///     <c>await</c> boundaries. Together they enrich every log line without threading values
///     through method signatures. This sample is fully in-process — it prints the aggregated
///     context directly so the effect is visible.
/// </summary>
public static class ContextManagerSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Context manager & LogContextScope ---");

        // The ContextManager registers built-in providers and exposes their merged view.
        var contextManager = new ContextManager();
        contextManager.RegisterProvider(new MachineContextProvider());
        contextManager.RegisterProvider(new ProcessContextProvider());
        contextManager.RegisterProvider(new SampleTenantContextProvider("acme-corp"));

        Console.WriteLine($"Registered providers: {contextManager.ProviderCount}");

        var ambient = contextManager.GetContextProperties();
        Console.WriteLine("Aggregated ambient context:");
        foreach (var (key, value) in ambient.OrderBy(p => p.Key))
        {
            Console.WriteLine($"  {key} = {value}");
        }

        // LogContextScope adds per-operation properties via AsyncLocal. Nested scopes merge
        // with their parent and are automatically restored on Dispose.
        using (LogContextScope.PushProperty("CorrelationId", Guid.NewGuid().ToString("N")[..8]))
        {
            Console.WriteLine($"\nOuter scope CorrelationId = {LogContextScope.GetProperty("CorrelationId")}");

            using (LogContextScope.PushContext(new Dictionary<string, object?>
            {
                ["Operation"] = "CreateInvoice",
                ["InvoiceId"] = "INV-9001",
            }))
            {
                // Child sees both its own and the inherited outer property.
                Console.WriteLine($"Inner scope CorrelationId (inherited) = {LogContextScope.GetProperty("CorrelationId")}");
                Console.WriteLine($"Inner scope Operation = {LogContextScope.GetProperty<string>("Operation")}");
            }

            // Inner scope disposed — its properties are gone, the outer one remains.
            Console.WriteLine($"After inner scope, Operation = {LogContextScope.GetProperty("Operation") ?? "<none>"}");
        }

        Console.WriteLine($"After outer scope, CorrelationId = {LogContextScope.GetProperty("CorrelationId") ?? "<none>"}");
    }

    /// <summary>A minimal custom context provider supplying a static tenant id.</summary>
    private sealed class SampleTenantContextProvider(string tenantId) : ContextProviderBase("Tenant", priority: 10)
    {
        public override bool IsAvailable() => true;

        public override IReadOnlyDictionary<string, object?> GetContextProperties()
            => CreatePropertiesDictionary(("TenantId", tenantId));
    }
}
