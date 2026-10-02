using Pragmatic.Notes;

namespace Pragmatic.Notes.Samples.Samples;

/// <summary>
/// Demonstrates the runnable runtime surface of <see cref="HasNotesAttribute"/>:
/// its four configurable options and the meaning of the
/// <see cref="HasNotesAttribute.EditWindowMinutes"/> sentinel values.
/// </summary>
/// <remarks>
/// entity, repository, actions or endpoints are emitted. What ships today is only this options
/// attribute and the <see cref="NoteBase{TEntityId}"/> base type. The attribute is constructed
/// directly here (no reflection — the SG would read the same property values at compile time).
/// <para>
/// EditWindowMinutes semantics (from the attribute docs / README): the default is <c>-1</c>, which
/// means "no limit" — editing is allowed for the full lifetime of the note. A value of <c>0</c>
/// means no window at all (edits are effectively closed immediately), and a positive value caps
/// editing to that many minutes after creation. <see cref="HasNotesAttribute.AllowEditing"/>
/// is the master switch: when false, the window value is moot.
/// </para>
/// </remarks>
public static class HasNotesOptionsSample
{
    // Parent entities the attribute is generic over.
    private sealed class Ticket;

    private sealed class Order;

    private sealed class AuditLog;

    public static void Run()
    {
        Console.WriteLine("=== HasNotes attribute options ===");

        // Defaults: MaxLength=4000, AllowEditing=true, EditWindowMinutes=-1 (no limit), SubBoundary=null.
        PrintConfig("Ticket   (defaults)", new HasNotesAttribute());

        // Custom length + a finite 15-minute edit window after creation + custom sub-boundary name.
        PrintConfig(
            "Order    (15-min window)",
            new HasNotesAttribute
            {
                MaxLength = 500,
                AllowEditing = true,
                EditWindowMinutes = 15,
                SubBoundary = "OrderInternalNotes",
            });

        // EditWindowMinutes = 0: editing allowed in principle, but the window is closed immediately.
        PrintConfig(
            "Order    (zero window)",
            new HasNotesAttribute { AllowEditing = true, EditWindowMinutes = 0 });

        // Editing disabled outright: the window value is irrelevant.
        PrintConfig(
            "AuditLog (editing off)",
            new HasNotesAttribute { MaxLength = 2000, AllowEditing = false, EditWindowMinutes = 15 });
    }

    private static void PrintConfig(string label, HasNotesAttribute attr)
    {
        Console.WriteLine($"  {label}");
        Console.WriteLine($"    MaxLength         = {attr.MaxLength}");
        Console.WriteLine($"    AllowEditing      = {attr.AllowEditing}");
        Console.WriteLine($"    EditWindowMinutes = {attr.EditWindowMinutes}  -> {DescribeEditWindow(attr)}");
        Console.WriteLine($"    SubBoundary       = {attr.SubBoundary ?? "<default: {Parent}Notes>"}");
    }

    /// <summary>
    /// Resolves the documented EditWindowMinutes semantics:
    /// AllowEditing=false disables editing entirely; otherwise -1 = no limit, 0 = no window,
    /// and a positive value = that many minutes after creation.
    /// </summary>
    private static string DescribeEditWindow(HasNotesAttribute attr)
        => attr switch
        {
            { AllowEditing: false } => "AllowEditing=false -> editing disabled (window irrelevant)",
            { EditWindowMinutes: -1 } => "no limit (editable for the note's whole lifetime)",
            { EditWindowMinutes: 0 } => "zero window (edits effectively closed immediately)",
            { EditWindowMinutes: var m } when m > 0 => $"editable for {m} minute(s) after creation",
            { EditWindowMinutes: var m } => $"unexpected sentinel ({m})",
        };
}
