using Pragmatic.Testing.Assertions;
using Pragmatic;
using Pragmatic.Internationalization;
using Pragmatic.Internationalization.Humanizer;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Tests;
using Pragmatic.Internationalization.Tests.Humanizer;
using Pragmatic.Internationalization.Types;
using Xunit;
using Pragmatic.Testing.Mocking;

// Every type this test assembly mocks. The generator turns each into a {Type}Mock class,
// so the declarations double as the inventory: what this suite stands in for, in one place.
[assembly: GenerateMock<IStringLocalizer>]
