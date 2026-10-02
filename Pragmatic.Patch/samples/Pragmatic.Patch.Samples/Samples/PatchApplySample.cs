namespace Pragmatic.Patch.Samples.Samples;

/// <summary>
///     Generated ApplyTo() and ModifiedProperties on a real entity.
///     Shows partial update, null clearing, and property exclusions.
/// </summary>
public static class PatchApplySample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("2. Patch ApplyTo — Generated Partial Update");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowPartialUpdate();
        ShowNullClearing();
        ShowPropertyExclusions();
        ShowModifiedProperties();

        Console.WriteLine();
    }

    private static void ShowPartialUpdate()
    {
        Console.WriteLine("  2.1 Partial update — only sent fields are applied");
        Console.WriteLine("  ---------------------------------------------------");

        var guest = new Guest
        {
            Id = Guid.NewGuid(),
            FirstName = "Mario",
            LastName = "Rossi",
            Email = "mario@example.com",
            Phone = "+39 02 1234567",
            CreatedAt = DateTime.UtcNow
        };

        // Simulate a PATCH request that only updates Email
        var patch = new PatchGuest
        {
            Email = "mario.rossi@newdomain.com"
            // FirstName, LastName, Phone → Undefined (not sent)
        };

        Console.WriteLine($"    Before: FirstName=\"{guest.FirstName}\", Email=\"{guest.Email}\"");
        patch.ApplyTo(guest);
        Console.WriteLine($"    After:  FirstName=\"{guest.FirstName}\" (unchanged), Email=\"{guest.Email}\" (updated)");
        Console.WriteLine();
    }

    private static void ShowNullClearing()
    {
        Console.WriteLine("  2.2 Null clearing — explicitly set to null via Optional<T>.Null");
        Console.WriteLine("  ----------------------------------------------------------------");

        var guest = new Guest
        {
            Id = Guid.NewGuid(),
            FirstName = "Anna",
            LastName = "Verdi",
            Phone = "+39 06 9876543"
        };

        // Simulate: { "phone": null } — explicitly clear the phone
        var patch = new PatchGuest
        {
            Phone = Optional<string>.Null
        };

        Console.WriteLine($"    Before: Phone=\"{guest.Phone}\"");
        patch.ApplyTo(guest);
        Console.WriteLine($"    After:  Phone={(guest.Phone is null ? "(null — cleared)" : guest.Phone)}");
        Console.WriteLine();
    }

    private static void ShowPropertyExclusions()
    {
        Console.WriteLine("  2.3 Property exclusions — Id, audit fields are auto-excluded");
        Console.WriteLine("  ---------------------------------------------------------------");

        Console.WriteLine("    Guest entity properties vs PatchGuest generated properties:");
        Console.WriteLine("      Id          → EXCLUDED (identity)");
        Console.WriteLine("      FirstName   → INCLUDED (Optional<string>)");
        Console.WriteLine("      LastName    → INCLUDED (Optional<string>)");
        Console.WriteLine("      Email       → INCLUDED (Optional<string?>)");
        Console.WriteLine("      Phone       → INCLUDED (Optional<string?>)");
        Console.WriteLine("      VipLevel    → INCLUDED (Optional<int>, uses SetVipLevel())");
        Console.WriteLine("      CreatedAt   → EXCLUDED (audit)");
        Console.WriteLine("      CreatedBy   → EXCLUDED (audit)");
        Console.WriteLine("      UpdatedAt   → EXCLUDED (audit)");
        Console.WriteLine("      UpdatedBy   → EXCLUDED (audit)");
        Console.WriteLine();
    }

    private static void ShowModifiedProperties()
    {
        Console.WriteLine("  2.4 ModifiedProperties — track what changed");
        Console.WriteLine("  -----------------------------------------------");

        var patch = new PatchGuest
        {
            FirstName = "Updated",
            Email = Optional<string>.Null,
            VipLevel = 5
            // LastName, Phone → Undefined
        };

        Console.WriteLine($"    Fields sent: FirstName, Email (null), VipLevel");
        Console.WriteLine($"    Fields NOT sent: LastName, Phone");
        Console.WriteLine($"    ModifiedProperties: [{string.Join(", ", patch.ModifiedProperties)}]");
        Console.WriteLine($"    Count: {patch.ModifiedProperties.Count}");
        Console.WriteLine();

        Console.WriteLine("    Use case: pass ModifiedProperties to change-aware validation");
        Console.WriteLine("    or to Persistence for selective SQL UPDATE.");
        Console.WriteLine();
    }
}
