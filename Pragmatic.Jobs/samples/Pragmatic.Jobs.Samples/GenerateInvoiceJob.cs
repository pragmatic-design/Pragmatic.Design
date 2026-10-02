using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Jobs.Samples;

/// <summary>
///     Example: delayed job with typed parameters and continuation.
///     After invoice generation completes, SendInvoiceEmailJob runs automatically.
/// </summary>
public record InvoiceParams(Guid ReservationId, Guid GuestId, decimal Amount, string Currency);

[Job]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 2000)]
[Timeout(TimeoutSeconds = 120)]
[Continuation<SendInvoiceEmailJob>]
public sealed partial class GenerateInvoiceJob : IJob<InvoiceParams>
{
    public Task ExecuteAsync(InvoiceParams p, JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"Generating invoice for reservation {p.ReservationId}: {p.Amount} {p.Currency}");
        // In real code: billingActions.CreateDraftInvoice(...)
        return Task.CompletedTask;
    }
}

/// <summary>
///     Continuation job: sends the invoice email after generation.
///     Automatically enqueued when GenerateInvoiceJob completes.
/// </summary>
[Job]
[Retry(MaxAttempts = 2)]
public sealed partial class SendInvoiceEmailJob : IJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"Sending invoice email (correlation: {context.CorrelationId})");
        return Task.CompletedTask;
    }
}
