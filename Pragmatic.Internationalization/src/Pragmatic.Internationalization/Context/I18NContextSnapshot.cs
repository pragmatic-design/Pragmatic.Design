using System.Globalization;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Represents a snapshot of the I18N context state at a point in time.
///     Used by <see cref="I18NContext.Capture"/> and <see cref="I18NContext.Restore"/>
///     for test isolation.
/// </summary>
/// <param name="Context">The captured I18N context, or null if no context was set.</param>
/// <param name="ThreadUICulture">The thread's UI culture at capture time.</param>
/// <param name="ThreadCulture">The thread's culture at capture time.</param>
public sealed record I18NContextSnapshot(
    I18NContext? Context,
    CultureInfo ThreadUICulture,
    CultureInfo ThreadCulture);
