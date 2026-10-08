// Native AOT smoke for declared redaction: a record with a [PersonalData] member, logged through the
// JSON provider with the generated redaction map, must come out with that member masked. Under the JIT
// that is what the redaction tests already show; this is the same entry published Native AOT.
//
// The second entry is the case the generator cannot describe: a declared type with an `object` member
// gets no JSON metadata, and under Native AOT nothing else can serialize it. The entry must still be
// written, with the whole value masked and counted, rather than lost or sent out in clear.
//
// The third goes through a generated log call site with a [PersonalData] parameter: its state writes
// itself as UTF-8 and masks the argument, with no serializer and no reflection anywhere on the path.
//
// The fourth is a call site whose argument is the record itself: a generated writer writes it, with the
// declared member masked, still with no serializer.

using System.Text;
using Microsoft.Extensions.Logging;
using Pragmatic.Aot.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Privacy;
using Pragmatic.Redaction;

var output = new MemoryStream();
var redactor = new DeclaredRedactor([new Pragmatic.Aot.Logging.Generated.GeneratedRedactionMap()]);
var provider = new PragmaticJsonProvider("aot-json", PragmaticJsonConfiguration.ForJson(), output)
{
    DeclaredRedactor = redactor,
};

// Through an ILogger, as an application logs: the provider's own logger materializes the entry,
// applies declared redaction, and hands it to the JSON writer.
var logger = provider.CreateLogger("Smoke");
logger.LogInformation("registered {Customer}", new Customer("C-42", "jane@example.com"));
logger.LogInformation("paid {Payment}", new Payment { Reference = "P-7", Iban = "DE89370400440532013000", Note = "x" });
provider.Dispose();

var written = Encoding.UTF8.GetString(output.ToArray());
var lines = written.Split('\n', StringSplitOptions.RemoveEmptyEntries);
var customer = lines.FirstOrDefault(l => l.Contains("registered", StringComparison.Ordinal)) ?? "";
var payment = lines.FirstOrDefault(l => l.Contains("paid", StringComparison.Ordinal)) ?? "";

var masked = customer.Contains(PersonalDataPatterns.Mask, StringComparison.Ordinal);
var leaked = written.Contains("jane@example.com", StringComparison.Ordinal);
var kept = customer.Contains("C-42", StringComparison.Ordinal);

var paymentWritten = payment.Length > 0;
var paymentLeaked = written.Contains("DE89370400440532013000", StringComparison.Ordinal);
var paymentCounted = redactor.ValuesWithoutMetadata > 0;

// Context enrichment off: with it on the entry is materialized, and the call-site path is the one under test.
var callSiteOutput = new MemoryStream();
var callSiteConfiguration = PragmaticJsonConfiguration.ForJson();
callSiteConfiguration.IncludeContextEnrichment = false;
var callSiteProvider = new PragmaticJsonProvider("aot-callsite", callSiteConfiguration, callSiteOutput);
CallSiteLog.ReceiptSent(callSiteProvider.CreateLogger("Smoke"), 42, "alice@example.com");
CallSiteLog.Registered(callSiteProvider.CreateLogger("Smoke"), new Customer("C-77", "bob@example.com"));
callSiteProvider.Dispose();

var callSiteLines = Encoding.UTF8.GetString(callSiteOutput.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
var receipt = callSiteLines.FirstOrDefault(l => l.Contains("Receipt", StringComparison.Ordinal))?.Trim() ?? "";
var receiptMasked = receipt.Contains("\"@message\":\"Receipt for 42 sent to [redacted]\"", StringComparison.Ordinal)
                    && receipt.Contains("\"Email\":\"[redacted]\"", StringComparison.Ordinal);
var receiptLeaked = receipt.Contains("alice@example.com", StringComparison.Ordinal);

var registered = callSiteLines.FirstOrDefault(l => l.Contains("Registered", StringComparison.Ordinal))?.Trim() ?? "";
var registeredMasked = registered.Contains("C-77", StringComparison.Ordinal)
                       && registered.Contains(PersonalDataPatterns.Mask, StringComparison.Ordinal);
var registeredLeaked = registered.Contains("bob@example.com", StringComparison.Ordinal);

if (!masked || leaked || !kept || !paymentWritten || paymentLeaked || !paymentCounted || !receiptMasked || receiptLeaked
    || !registeredMasked || registeredLeaked)
{
    Console.Error.WriteLine(
        $"AOT-LOGGING-FAIL: masked={masked} leaked={leaked} kept={kept} paymentWritten={paymentWritten} "
        + $"paymentLeaked={paymentLeaked} paymentCounted={paymentCounted} receiptMasked={receiptMasked} receiptLeaked={receiptLeaked} "
        + $"registeredMasked={registeredMasked} registeredLeaked={registeredLeaked}; "
        + $"last error={provider.GetMetrics().LastError ?? callSiteProvider.GetMetrics().LastError ?? "(none)"}; "
        + $"written: {(written.Length == 0 ? "(nothing)" : written)} | {string.Join(" | ", callSiteLines)}");
    return 1;
}

Console.WriteLine($"AOT-LOGGING-OK: {customer.Trim()} | {payment.Trim()} | {receipt} | {registered}");
return 0;

namespace Pragmatic.Aot.Logging
{
    /// <summary>A customer, whose e-mail is personal data and must never reach a log in clear.</summary>
    public sealed record Customer(string Reference, [property: PersonalData(DataCategory.Contact)] string Email);

    /// <summary>
    ///     A payment the generator cannot describe: the <c>object</c> member defers its JSON shape, so under
    ///     Native AOT there is no metadata to serialize it with.
    /// </summary>
    public sealed class Payment
    {
        public string Reference { get; set; } = "";

        [PersonalData(DataCategory.Financial)]
        public string Iban { get; set; } = "";

        public object? Note { get; set; }
    }

    /// <summary>A generated call site whose argument is personal data.</summary>
    public static partial class CallSiteLog
    {
        [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Receipt for {OrderId} sent to {Email}")]
        public static partial void ReceiptSent(ILogger logger, int orderId, [PersonalData(DataCategory.Contact)] string email);

        [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Registered {Customer}")]
        public static partial void Registered(ILogger logger, Customer customer);
    }
}
