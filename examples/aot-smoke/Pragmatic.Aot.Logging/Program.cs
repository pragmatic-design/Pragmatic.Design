// Native AOT smoke for declared redaction: a record with a [PersonalData] member, logged through the
// JSON provider with the generated redaction map, must come out with that member masked. Under the JIT
// that is what the redaction tests already show; this is the same entry published Native AOT.
//
// The second entry is the case the generator cannot describe: a declared type with an `object` member
// gets no JSON metadata, and under Native AOT nothing else can serialize it. The entry must still be
// written, with the whole value masked and counted, rather than lost or sent out in clear.

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

if (!masked || leaked || !kept || !paymentWritten || paymentLeaked || !paymentCounted)
{
    Console.Error.WriteLine(
        $"AOT-LOGGING-FAIL: masked={masked} leaked={leaked} kept={kept} paymentWritten={paymentWritten} "
        + $"paymentLeaked={paymentLeaked} paymentCounted={paymentCounted}; "
        + $"last error={provider.GetMetrics().LastError ?? "(none)"}; written: {(written.Length == 0 ? "(nothing)" : written)}");
    return 1;
}

Console.WriteLine($"AOT-LOGGING-OK: {customer.Trim()} | {payment.Trim()}");
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
}
