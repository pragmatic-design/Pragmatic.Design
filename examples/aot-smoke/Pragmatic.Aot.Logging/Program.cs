// Native AOT smoke for declared redaction: a record with a [PersonalData] member, logged through the
// JSON provider with the generated redaction map, must come out with that member masked. Under the JIT
// that is what the redaction tests already show; this is the same entry published Native AOT.

using System.Text;
using Microsoft.Extensions.Logging;
using Pragmatic.Aot.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Privacy;
using Pragmatic.Redaction;

var output = new MemoryStream();
var provider = new PragmaticJsonProvider("aot-json", PragmaticJsonConfiguration.ForJson(), output)
{
    DeclaredRedactor = new DeclaredRedactor([new Pragmatic.Aot.Logging.Generated.GeneratedRedactionMap()]),
};

// Through an ILogger, as an application logs: the provider's own logger materializes the entry,
// applies declared redaction, and hands it to the JSON writer.
provider.CreateLogger("Smoke").LogInformation("registered {Customer}", new Customer("C-42", "jane@example.com"));
provider.Dispose();

var written = Encoding.UTF8.GetString(output.ToArray());
var masked = written.Contains(PersonalDataPatterns.Mask, StringComparison.Ordinal);
var leaked = written.Contains("jane@example.com", StringComparison.Ordinal);
var kept = written.Contains("C-42", StringComparison.Ordinal);

if (!masked || leaked || !kept)
{
    Console.Error.WriteLine(
        $"AOT-LOGGING-FAIL: masked={masked} leaked={leaked} kept={kept}; last error={provider.GetMetrics().LastError ?? "(none)"}; written: {(written.Length == 0 ? "(nothing)" : written)}");
    return 1;
}

Console.WriteLine($"AOT-LOGGING-OK: {written.Trim()}");
return 0;

namespace Pragmatic.Aot.Logging
{
    /// <summary>A customer, whose e-mail is personal data and must never reach a log in clear.</summary>
    public sealed record Customer(string Reference, [property: PersonalData(DataCategory.Contact)] string Email);
}
