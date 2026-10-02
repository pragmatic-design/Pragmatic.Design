using Pragmatic.Ensure.Result;
using Pragmatic.Result;

namespace Pragmatic.Ensure.Samples.Samples;

/// <summary>
///     Demonstrates the Result-returning <c>Check.*</c> API: composing multiple
///     validations into a single short-circuiting pipeline with <c>Then</c>, plus the
///     string/collection guards <c>DoesNotContain</c>, <c>DoesNotStartWith</c>,
///     <c>DoesNotEndWith</c>, and <c>NoDuplicates</c>.
/// </summary>
/// <remarks>
///     <c>Check.*</c> returns <see cref="VoidResult{TError}" /> for <i>expected</i>
///     validation failures (user input, business rules), whereas <c>Ensure.ThrowIf*</c>
///     throws for programming errors. <c>VoidResult&lt;TError&gt;.Then</c> chains the next
///     check only when the previous one succeeded (short-circuit composition).
/// </remarks>
public static class CheckBindSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. Check.* Result API — Then Chaining & String/Collection Guards");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowThenChaining();
        ShowStringGuards();
        ShowCollectionGuards();

        Console.WriteLine();
    }

    private static void ShowThenChaining()
    {
        Console.WriteLine("  7.1 Then — multi-step validation pipeline (short-circuits on first failure)");
        Console.WriteLine("  ----------------------------------------------------------------------------");

        // Valid registration — every step passes, pipeline succeeds.
        var ok = ValidateRegistration("Ada Lovelace", "ada@example.com", "s3cret-passphrase");
        Console.WriteLine($"    Valid input            → {Describe(ok)}");

        // Empty name — first step fails, later steps never run.
        var emptyName = ValidateRegistration("", "ada@example.com", "s3cret-passphrase");
        Console.WriteLine($"    Empty name             → {Describe(emptyName)}");

        // Bad email — second step fails.
        var badEmail = ValidateRegistration("Ada Lovelace", "not-an-email", "s3cret-passphrase");
        Console.WriteLine($"    Bad email              → {Describe(badEmail)}");

        // Short password — third step fails.
        var shortPwd = ValidateRegistration("Ada Lovelace", "ada@example.com", "123");
        Console.WriteLine($"    Short password         → {Describe(shortPwd)}");

        Console.WriteLine();
    }

    /// <summary>
    ///     Composes four checks into one pipeline. Each <c>Then</c> only runs when the
    ///     previous step succeeded, so the first failing rule wins.
    /// </summary>
    private static VoidResult<SampleError> ValidateRegistration(string name, string email, string password)
    {
        return Check.NotNullOrWhiteSpace(name, new SampleError("NAME_REQUIRED", "Name is required."))
            .Then(() => Check.Email(email, new SampleError("EMAIL_INVALID", "Email is not a valid address.")))
            .Then(() => Check.LengthInRange(password, 8, 64,
                new SampleError("PASSWORD_LENGTH", "Password must be 8-64 characters.")))
            .Then(() => Check.DoesNotContain(password, " ",
                new SampleError("PASSWORD_SPACES", "Password must not contain spaces.")));
    }

    private static void ShowStringGuards()
    {
        Console.WriteLine("  7.2 String guards — DoesNotContain / DoesNotStartWith / DoesNotEndWith");
        Console.WriteLine("  -----------------------------------------------------------------------");

        const string slug = "valid-product-slug";

        var noSpaces = Check.DoesNotContain(slug, " ", new SampleError("SLUG_SPACES", "Slug must not contain spaces."));
        Console.WriteLine($"    DoesNotContain(\"{slug}\", \" \")          → {Describe(noSpaces)}");

        var badPrefix = Check.DoesNotStartWith("__internal", "__",
            new SampleError("SLUG_PREFIX", "Slug must not start with '__'."));
        Console.WriteLine($"    DoesNotStartWith(\"__internal\", \"__\")    → {Describe(badPrefix)}");

        var badSuffix = Check.DoesNotEndWith("backup.tmp", ".tmp",
            new SampleError("FILE_SUFFIX", "File must not end with '.tmp'."));
        Console.WriteLine($"    DoesNotEndWith(\"backup.tmp\", \".tmp\")    → {Describe(badSuffix)}");

        Console.WriteLine();
    }

    private static void ShowCollectionGuards()
    {
        Console.WriteLine("  7.3 Collection guard — NoDuplicates");
        Console.WriteLine("  ------------------------------------");

        string[] uniqueTags = ["sql", "csharp", "dotnet"];
        var unique = Check.NoDuplicates(uniqueTags, new SampleError("TAGS_DUP", "Tags must be unique."));
        Console.WriteLine($"    NoDuplicates([sql, csharp, dotnet])    → {Describe(unique)}");

        string[] dupedTags = ["sql", "csharp", "sql"];
        var duped = Check.NoDuplicates(dupedTags, new SampleError("TAGS_DUP", "Tags must be unique."));
        Console.WriteLine($"    NoDuplicates([sql, csharp, sql])       → {Describe(duped)}");

        Console.WriteLine();
    }

    private static string Describe(VoidResult<SampleError> result)
        => result.Match(
            onSuccess: () => "SUCCESS",
            onFailure: error => $"FAILURE: {error.Message}");

    /// <summary>
    ///     Minimal domain error used by the Check.* samples. Extends the
    ///     <see cref="Error" /> abstract record from Pragmatic.Result.
    /// </summary>
    private sealed record SampleError(string Code, string Message) : Error
    {
        public override string Code { get; } = Code;

        public override int StatusCode => 400;
    }
}
