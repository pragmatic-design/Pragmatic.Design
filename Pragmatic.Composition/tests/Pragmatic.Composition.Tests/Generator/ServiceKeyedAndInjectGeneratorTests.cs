// Pragmatic.Composition.Tests - [Service<T>] Key (keyed service registration) generator output tests.
//
// [Inject]: ServiceTransform matches the inject attribute by its FQN
// "Pragmatic.Composition.Attributes.InjectAttribute", so [Inject] members are detected and
// ServiceRegistrationTemplate emits the ActivatorUtilities.CreateInstance factory wiring. A wrong FQN
// would make [Inject] a silent no-op; the tests below pin that behaviour.

using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Tests.Helpers;
using Xunit;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Tests for keyed-service registration generation via [Service] / [Service&lt;T&gt;] with a Key.
///     Verifies the exact registration code shape emitted by ServiceRegistrationTemplate.
/// </summary>
public class ServiceKeyedAndInjectGeneratorTests
{
    [Fact]
    public void Generator_KeyedService_GeneratesAddKeyedScopedWithKey()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IDatabase { }

            [Service(Key = "primary")]
            public class PrimaryDatabase : IDatabase { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        content.Should().Contain(
            "AddKeyedScoped<global::TestApp.IDatabase, global::TestApp.PrimaryDatabase>(\"primary\")");
    }

    [Fact]
    public void Generator_KeyedSingleton_GeneratesAddKeyedSingleton()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface ICache { }

            [Service(Lifetime = Lifetime.Singleton, Key = "redis")]
            public class RedisCache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        content.Should().Contain(
            "AddKeyedSingleton<global::TestApp.ICache, global::TestApp.RedisCache>(\"redis\")");
    }

    [Fact]
    public void Generator_KeyedTransient_GeneratesAddKeyedTransient()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IHandler { }

            [Service(Lifetime = Lifetime.Transient, Key = "fast")]
            public class FastHandler : IHandler { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        content.Should().Contain(
            "AddKeyedTransient<global::TestApp.IHandler, global::TestApp.FastHandler>(\"fast\")");
    }

    [Fact]
    public void Generator_GenericServiceAttributeWithKey_GeneratesKeyedRegistrationForExplicitInterface()
    {
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IPaymentProvider { }
            public interface IConfigurable { }

            [Service<IPaymentProvider>(Key = "stripe")]
            public class StripeProvider : IPaymentProvider, IConfigurable { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        // Keyed registration against the explicitly-chosen interface, not IConfigurable.
        content.Should().Contain(
            "AddKeyedScoped<global::TestApp.IPaymentProvider, global::TestApp.StripeProvider>(\"stripe\")");
        content.Should().NotContain("IConfigurable");
    }

    [Fact]
    public void Generator_NonKeyedService_DoesNotEmitKeyedRegistration()
    {
        // Sanity counterpart: without a Key, registration is the plain (non-keyed) form.
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IPlain { }

            [Service]
            public class PlainService : IPlain { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        content.Should().Contain("TryAddScoped<global::TestApp.IPlain, global::TestApp.PlainService>()");
        content.Should().NotContain("AddKeyed");
    }

    [Fact]
    public void Generator_ServiceWithInject_GeneratesFactoryWiring()
    {
        // [Inject] must be detected and produce factory wiring.
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IClock { }
            public interface IReport { }

            [Service]
            public class Report : IReport
            {
                [Inject(Required = true)]
                public IClock Clock { get; set; } = null!;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        content.Should().Contain("ActivatorUtilities.CreateInstance<global::TestApp.Report>(sp)");
        content.Should().Contain("instance.Clock = sp.GetRequiredService<global::TestApp.IClock>();");
    }

    [Fact]
    public void Generator_KeyedServiceWithInject_GeneratesKeyedFactory()
    {
        // A factory-registered ([Inject]) service that
        // also has a Key must be registered KEYED, not lose the key.
        var source = """
            using Pragmatic.Composition.Attributes;

            namespace TestApp;

            public interface IClock { }
            public interface IReport { }

            [Service(Key = "main")]
            public class Report : IReport
            {
                [Inject]
                public IClock? Clock { get; set; }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        content.Should().Contain("AddKeyedScoped<global::TestApp.IReport>(\"main\", (sp, _) =>");
        content.Should().Contain("ActivatorUtilities.CreateInstance<global::TestApp.Report>(sp)");
    }
}
