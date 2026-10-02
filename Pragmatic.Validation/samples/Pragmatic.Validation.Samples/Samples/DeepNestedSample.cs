namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     Deep nested validation: Invoice with nested address + collection of lines,
///     custom MessageKey overrides, and multi-level error path tracking.
/// </summary>
public static class DeepNestedSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. Deep Nested Validation & Custom Message Keys");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowValidInvoice();
        ShowInvalidNestedAddress();
        ShowInvalidLines();
        ShowCustomMessageKeys();

        Console.WriteLine();
    }

    private static void ShowValidInvoice()
    {
        Console.WriteLine("  8.1 Valid invoice with nested address + lines");
        Console.WriteLine("  -----------------------------------------------");

        var invoice = new CreateInvoiceRequest
        {
            CustomerId = "cust-42",
            InvoiceRef = "INV-2025-001",
            BillingAddress = new InvoiceAddressRequest
            {
                Street = "Via Roma 42",
                City = "Milano",
                PostalCode = "20100"
            },
            Lines =
            [
                new InvoiceLineRequest { Description = "Consulting", Quantity = 10, UnitPrice = 150m },
                new InvoiceLineRequest { Description = "Travel", Quantity = 1, UnitPrice = 350m }
            ]
        };

        var result = invoice.Validate();
        Console.WriteLine($"    IsSuccess: {result.IsSuccess}");
        Console.WriteLine();
    }

    private static void ShowInvalidNestedAddress()
    {
        Console.WriteLine("  8.2 Invalid nested address — errors show object path");
        Console.WriteLine("  -------------------------------------------------------");

        var invoice = new CreateInvoiceRequest
        {
            CustomerId = "cust-42",
            InvoiceRef = "INV-2025-001",
            BillingAddress = new InvoiceAddressRequest
            {
                Street = "AB", // MinLength(3)
                City = "",     // Required
                PostalCode = "ABCDE" // Regex: must be 5 digits
            },
            Lines = [new InvoiceLineRequest { Description = "Item", Quantity = 1, UnitPrice = 10m }]
        };

        var result = invoice.Validate();
        Console.WriteLine($"    BillingAddress: Street=\"AB\", City=\"\", PostalCode=\"ABCDE\"");
        Console.WriteLine($"    Issues ({result.Count}):");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }

    private static void ShowInvalidLines()
    {
        Console.WriteLine("  8.3 Invalid lines — errors show indexed collection path");
        Console.WriteLine("  ---------------------------------------------------------");

        var invoice = new CreateInvoiceRequest
        {
            CustomerId = "cust-42",
            InvoiceRef = "INV-2025-001",
            BillingAddress = new InvoiceAddressRequest
            {
                Street = "Via Roma 42", City = "Milano", PostalCode = "20100"
            },
            Lines =
            [
                new InvoiceLineRequest { Description = "OK Item", Quantity = 5, UnitPrice = 100m },
                new InvoiceLineRequest { Description = "", Quantity = 0, UnitPrice = -10m },
                new InvoiceLineRequest { Description = "Another OK", Quantity = 1, UnitPrice = 50m }
            ]
        };

        var result = invoice.Validate();
        Console.WriteLine($"    Line[0]: valid, Line[1]: Description=\"\", Qty=0, Price=-10, Line[2]: valid");
        Console.WriteLine($"    Issues ({result.Count}):");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }

    private static void ShowCustomMessageKeys()
    {
        Console.WriteLine("  8.4 Custom MessageKey — override default validation messages");
        Console.WriteLine("  ---------------------------------------------------------------");

        var invoice = new CreateInvoiceRequest
        {
            CustomerId = "",         // Custom: "invoice.customer_required"
            InvoiceRef = "AB",       // Custom: "invoice.ref_too_short"
            BillingAddress = new InvoiceAddressRequest
            {
                Street = "Via Roma 42",
                City = "Roma",
                PostalCode = "abc" // Custom: "address.invalid_postal_code"
            },
            Lines =
            [
                new InvoiceLineRequest { Description = "Item", Quantity = -1, UnitPrice = 10m }
                // Custom: "line.qty_must_be_positive"
            ]
        };

        var result = invoice.Validate();
        Console.WriteLine($"    Custom message keys in generated output:");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: \"{issue.MessageKey}\"");

        Console.WriteLine();
        Console.WriteLine("    MessageKey overrides enable i18n localization:");
        Console.WriteLine("    The key maps to a localized string in your resource files.");
        Console.WriteLine();
    }
}
