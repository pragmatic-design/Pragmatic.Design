using System.Globalization;
using System.Runtime.CompilerServices;

namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     Pins the process' default culture to <c>en-US</c> before any test runs, so that an assertion about
///     a translated answer can only be satisfied by the language the request asked for.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Without this, a localization assertion in this suite depends on the developer's machine.
///         A refusal is localized by <c>LocalizedErrorMessageResolver</c>, which reads
///         <c>I18NContext.Current</c>; where nothing has resolved a culture for the request that falls
///         back to <c>CultureInfo.CurrentCulture</c> — the machine's locale. On an Italian machine
///         <c>AnEmployee_IsRefusedInTheLanguageAsked</c> would therefore pass whatever the caller asked
///         for, while on <c>ubuntu-latest</c> the same test answers <c>"Not allowed"</c>, from
///         <c>en.json</c>.
///     </para>
///     <para>
///         The default thread culture is the lever, not <c>CultureInfo.CurrentCulture</c> in a test body:
///         the test server does not flow the caller's execution context into the request it handles
///         (<c>PreserveExecutionContext</c> is off), so the culture the application sees is the process
///         default. Setting it in a test looks like it reproduces the runner and does not — measured.
///     </para>
/// </remarks>
internal static class TheSuiteDoesNotInheritTheMachinesLocale
{
    [ModuleInitializer]
    internal static void Pin()
    {
        var neutralToThisRepository = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = neutralToThisRepository;
        CultureInfo.DefaultThreadCurrentUICulture = neutralToThisRepository;
    }
}
