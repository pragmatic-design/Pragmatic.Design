using Xunit;

namespace Pragmatic.Internationalization.Tests;

/// <summary>
///     Shared xUnit collection for tests that mutate process-global / ambient i18n state
///     (<c>I18NContext</c>, ambient <c>CultureInfo</c>). Membership serializes these classes
///     so they never run in parallel with each other, preventing AsyncLocal/culture races.
/// </summary>
[CollectionDefinition("I18nContext", DisableParallelization = true)]
public sealed class I18nContextCollection;
