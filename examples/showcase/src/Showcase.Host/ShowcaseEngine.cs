using Pragmatic.Composition;
using Pragmatic.Notifications.Configuration;

namespace Showcase.Host;

/// <summary>
///     What the host wires differently on each persistence engine. Program.cs is the same source for every Showcase host and calls these;
///     each host compiles one implementation of them: <c>Engine/ShowcaseEngine.EfCore.cs</c> here, and its own under
///     <c>examples/showcase/engines/</c> for the Sql and documents hosts, which leave this one out.
/// </summary>
/// <remarks>
///     Partial methods with an accessibility modifier must be implemented, so a host that compiles without its implementation does not
///     compile at all: nothing on this list can be left out by forgetting it.
/// </remarks>
internal static partial class ShowcaseEngine
{
    /// <summary>The boundaries' sessions, where the generated host does not register them itself.</summary>
    public static partial void UsePersistence(IPragmaticBuilder app);

    /// <summary>How the databases get their schema when the host starts.</summary>
    public static partial void UseSchema(IPragmaticBuilder app);

    /// <summary>Where the sent notifications are kept.</summary>
    public static partial void UseNotificationStore(NotificationsBuilder notifications, IPragmaticBuilder app);

    /// <summary>The per-user culture: the user's preferred culture read through the generated user resolver.</summary>
    public static partial void UseUserCulture(IPragmaticBuilder app);
}
