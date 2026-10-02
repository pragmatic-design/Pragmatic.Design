using Pragmatic.Actions.Invoker;
using Pragmatic.Resilience.Configuration;
using Pragmatic.Resilience.Samples.Actions;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Demonstrates [ResiliencePolicy] on DomainAction with the source generator.
///     The SG automatically injects IResiliencePipelineProvider into the generated invoker
///     and wraps Execute() with the named pipeline.
/// </summary>
public static class SourceGeneratorSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ 3. [ResiliencePolicy] on DomainAction — Live Demo ═══");
        Console.WriteLine();

        // ── Register resilience policies + action invokers ──
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPragmaticResilience(options =>
        {
            options.Policies["external-api"] = new ResiliencePolicyOptions
            {
                Timeout = new() { Timeout = TimeSpan.FromSeconds(5) },
                Retry = new()
                {
                    MaxRetries = 3,
                    BaseDelay = TimeSpan.FromMilliseconds(100),
                    BackoffType = BackoffType.Exponential
                }
            };

            options.Policies["payment-provider"] = new ResiliencePolicyOptions
            {
                Timeout = new() { Timeout = TimeSpan.FromSeconds(10) },
                Retry = new()
                {
                    MaxRetries = 2,
                    BaseDelay = TimeSpan.FromMilliseconds(200),
                    BackoffType = BackoffType.Constant
                },
                CircuitBreaker = new()
                {
                    FailureThreshold = 5,
                    BreakDuration = TimeSpan.FromSeconds(30)
                }
            };

            options.Policies["notifications"] = new ResiliencePolicyOptions
            {
                Timeout = new() { Timeout = TimeSpan.FromSeconds(2) }
            };
        });

        // Register via the SG-generated aggregate registration (calls AddScoped for each invoker)
        services.AddPragmaticActions();

        var sp = services.BuildServiceProvider();

        // ── Invoke actions through generated invokers ──
        // The SG wraps Execute() with the named resilience pipeline automatically

        Console.WriteLine("  FetchUserProfileAction [external-api: retry 3x + timeout 5s]:");
        var userInvoker = sp.GetRequiredService<IDomainActionInvoker<FetchUserProfileAction, string>>();
        var userResult = await userInvoker.InvokeAsync(new FetchUserProfileAction { UserId = "usr-42" });
        Console.WriteLine($"    Result: {(userResult.IsSuccess ? userResult.Value : userResult.Error.Code)}");
        Console.WriteLine();

        Console.WriteLine("  ProcessPaymentAction [payment-provider: retry 2x + circuit breaker]:");
        var paymentInvoker = sp.GetRequiredService<IDomainActionInvoker<ProcessPaymentAction, string>>();
        var paymentResult = await paymentInvoker.InvokeAsync(new ProcessPaymentAction { Amount = 149.99m });
        Console.WriteLine($"    Result: {(paymentResult.IsSuccess ? paymentResult.Value : paymentResult.Error.Code)}");
        Console.WriteLine();

        Console.WriteLine("  SendNotificationAction [notifications: timeout 2s]:");
        var notifyInvoker = sp.GetRequiredService<IVoidDomainActionInvoker<SendNotificationAction>>();
        var notifyResult = await notifyInvoker.InvokeAsync(new SendNotificationAction { Message = "Order confirmed!" });
        Console.WriteLine($"    Result: {(notifyResult.IsSuccess ? "Success" : notifyResult.Error.Code)}");
        Console.WriteLine();

        Console.WriteLine("  How it works:");
        Console.WriteLine("    1. [ResiliencePolicy(\"name\")] on the action class");
        Console.WriteLine("    2. SG detects attribute → injects IResiliencePipelineProvider in invoker");
        Console.WriteLine("    3. Generated ExecuteActionAsync wraps action.Execute with pipeline.ExecuteAsync");
        Console.WriteLine("    4. Named policy resolved from configuration at runtime");
        Console.WriteLine();
    }
}
