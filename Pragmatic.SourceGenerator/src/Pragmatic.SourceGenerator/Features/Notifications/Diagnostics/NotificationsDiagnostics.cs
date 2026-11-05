using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Notifications.Diagnostics;

/// <summary>
///     Diagnostic descriptors for the notification store declaration (PRAG2100-2149).
/// </summary>
internal static class NotificationsDiagnostics
{
    // PRAG2100: [StoresNotifications] on a boundary whose project does not reference the store package
    // of the boundary's engine: Pragmatic.Notifications.EFCore on EF Core, Pragmatic.Notifications.Engines
    // on the Sql and documents engines. The notification set is gated on that package, so the table is not
    // mapped, and the first send fails at run time with `relation "__Notifications" does not exist` —
    // which is what the attribute exists to prevent. Warning, like its four
    // siblings (PRAG2508, PRAG0831, PRAG0832, PRAG2752).
    public static readonly DiagnosticDescriptor StoresNotificationsWithoutEFCore = DiagnosticFactory.Warning(
        "PRAG2100",
        "[StoresNotifications] requires the notification store package of the boundary's engine",
        "Boundary '{0}' is marked [StoresNotifications] but this project does not reference {1}, so __Notifications is not mapped (the attribute is a no-op). Reference {1}.",
        "Reference the notification store package of the boundary's engine in the boundary project: Pragmatic.Notifications.EFCore on EF Core (the generated DbContext maps __Notifications), Pragmatic.Notifications.Engines on the Sql and documents engines (the generated Add{Boundary}Persistence registers the store, and the engine's schema holds __Notifications).");
}
