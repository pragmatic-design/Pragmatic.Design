using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Notifications.Diagnostics;

/// <summary>
///     Diagnostic descriptors for the notification store declaration (PRAG2100-2149).
/// </summary>
internal static class NotificationsDiagnostics
{
    // PRAG2100: [StoresNotifications] on a boundary whose project does not reference
    // Pragmatic.Notifications.EFCore. The notification set is gated on that package, so the table is not
    // mapped, and the first send fails at run time with `relation "__Notifications" does not exist` —
    // which is what the attribute exists to prevent. Warning, like its four
    // siblings (PRAG2508, PRAG0831, PRAG0832, PRAG2752).
    public static readonly DiagnosticDescriptor StoresNotificationsWithoutEFCore = DiagnosticFactory.Warning(
        "PRAG2100",
        "[StoresNotifications] requires Pragmatic.Notifications.EFCore",
        "Boundary '{0}' is marked [StoresNotifications] but this project does not reference Pragmatic.Notifications.EFCore, so __Notifications is not mapped (the attribute is a no-op). Reference Pragmatic.Notifications.EFCore.",
        "Add a reference to Pragmatic.Notifications.EFCore to the boundary project so the generated DbContext can map __Notifications.");
}
