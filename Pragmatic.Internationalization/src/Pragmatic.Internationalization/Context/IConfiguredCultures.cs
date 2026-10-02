using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     What the application was <b>configured</b> with: the default UI culture, and the cultures it
///     supports.
/// </summary>
/// <remarks>
///     <para>
///         The question a module asks is "what language does this application write in when nobody
///         said?", and it has exactly one correct answer — the host's <c>i18n.DefaultCulture(...)</c>.
///         This is the contract that answers it, and it answers nothing else: it is read-only and it
///         says nothing about the <em>current</em> culture, which is <c>I18NContext</c>'s business.
///     </para>
///     <para>
///         ⚠️ <b>Deliberately not <c>I18NConfigResolver</c>.</b> That class merges the registered
///         providers by priority and validates the result — work a module has no business holding. A
///         module that falls back on a constant for the cultures is honest and silently diverges the day
///         the host is configured differently; this interface is what it asks instead.
///     </para>
///     <para>
///         ⚠️ <b>And deliberately not <c>I18NContext.Current.Culture</c>.</b> With no scope open that
///         property falls back to <c>CultureInfo.CurrentCulture</c> — the thread's — and
///         <c>WithCultureAsync</c> documents that it does not restore the thread's culture after an
///         await, because the continuation may resume on another pool thread. So outside a scope it is
///         whatever the last piece of work on that thread left behind: measured as a letter that came
///         out in Italian for an applicant who named no language, because another test had rendered an
///         Italian one on the same thread. A job is in the same position as that test.
///     </para>
///     <para>
///         <b>Scoped</b>, like the resolver behind it: a configuration provider may be per-request — a
///         tenant's own default is the obvious one — so the answer is read inside the scope that asks.
///         A singleton holding it would hold the first scope's answer for every scope after it, which
///         is what <c>PRAG1642</c> reports.
///     </para>
/// </remarks>
[Pragmatic.Composition.Attributes.ProvidedByHost(Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IConfiguredCultures
{
    /// <summary>The default UI culture the host was configured with.</summary>
    /// <exception cref="I18NConfigurationException">
    ///     No provider configured one. The host refuses to start without a culture, so reading this
    ///     cannot invent one — see <c>UseI18N</c>.
    /// </exception>
    CultureCode Default { get; }

    /// <summary>
    ///     The cultures the host declared support for, or empty when it restricts nothing.
    /// </summary>
    IReadOnlyList<CultureCode> Supported { get; }
}
