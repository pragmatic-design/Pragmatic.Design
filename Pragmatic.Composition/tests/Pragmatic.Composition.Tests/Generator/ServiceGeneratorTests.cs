// Pragmatic.Composition.Tests - Service Generator Tests

using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Tests.Helpers;
using Xunit;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Tests for [Service] and [Decorator] attribute source generation.
/// </summary>
public class ServiceGeneratorTests
{
    [Fact]
    public void Generator_WithServiceAttribute_GeneratesRegistration()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IUserService { }

                     [Service]
                     public class UserService : IUserService { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Should().ContainKey("_Infra.DI.ServiceRegistration.g.cs");
        var content = output["_Infra.DI.ServiceRegistration.g.cs"];
        content.Should().Contain("AddPragmaticServices");
        content.Should().Contain("AddScoped<global::TestApp.IUserService, global::TestApp.UserService>");
    }

    /// <summary>
    ///     <c>[Service&lt;T&gt;]</c> where the service type is itself generic.
    /// </summary>
    /// <remarks>
    ///     A consumer reported that registering an <c>IActionFilter&lt;TAction&gt;</c> this way emitted
    ///     code that would not compile, and fell back to writing the AddScoped by hand. The type
    ///     argument has to be fully qualified like the outer type: an interface named without its
    ///     namespace resolves only by luck, depending on the usings of whatever file the registration
    ///     lands in.
    /// </remarks>
    [Fact]
    public void Generator_WithGenericServiceInterface_QualifiesTheTypeArgument()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp.Actions
                     {
                         public class UploadAction { }
                     }

                     namespace TestApp.Filters
                     {
                         public interface IActionFilter<TAction> { }

                         [Service<IActionFilter<TestApp.Actions.UploadAction>>]
                         public class UploadGuard : IActionFilter<TestApp.Actions.UploadAction> { }
                     }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        output["_Infra.DI.ServiceRegistration.g.cs"].Should().Contain(
            "global::TestApp.Filters.IActionFilter<global::TestApp.Actions.UploadAction>",
            "both the interface and its type argument must carry their namespace");
    }

    /// <summary>
    ///     The type argument names something another generator will create.
    /// </summary>
    /// <remarks>
    ///     A consumer registered <c>[Service&lt;IActionFilter&lt;UploadCaseFileAttachmentAction&gt;&gt;]</c>
    ///     where the action is emitted by the attachments trait, and got a registration that does not
    ///     compile. The symbol is an error type at the moment the attribute is read, and
    ///     FullyQualifiedFormat on an error type gives back the name as written — without its
    ///     namespace, and without global::. Same root as most of today's defects: a generator cannot
    ///     see what a generator will write.
    /// </remarks>
    [Fact]
    public void Generator_WithTypeArgumentFromAnotherGenerator_DoesNotEmitAnUnqualifiedName()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp.Filters
                     {
                         public interface IActionFilter<TAction> { }

                         [Service<IActionFilter<TestApp.Actions.UploadAction>>]
                         public class UploadGuard { }
                     }
                     """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);

        if (!output.TryGetValue("_Infra.DI.ServiceRegistration.g.cs", out var content))
            return;   // emitting nothing is a legitimate answer; emitting broken code is not

        content.Should().NotContain("IActionFilter<TestApp.Actions.UploadAction>>",
            "an unqualified type argument compiles only by accident of the usings in the generated file");
    }

    [Fact]
    public void Generator_WithSingletonLifetime_GeneratesSingletonRegistration()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface ICacheService { }

                     [Service(Lifetime = Lifetime.Singleton)]
                     public class CacheService : ICacheService { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Values.Should().Contain(s =>
            s.Contains("AddSingleton<global::TestApp.ICacheService, global::TestApp.CacheService>"));
    }

    [Fact]
    public void Generator_WithTransientLifetime_GeneratesTransientRegistration()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IProcessor { }

                     [Service(Lifetime = Lifetime.Transient)]
                     public class Processor : IProcessor { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Values.Should()
            .Contain(s => s.Contains("AddTransient<global::TestApp.IProcessor, global::TestApp.Processor>"));
    }

    [Fact]
    public void Generator_WithAsSelf_RegistersAsConcreteType()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Service(AsSelf = true)]
                     public class BackgroundWorker { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Values.Should().Contain(s =>
            s.Contains("AddScoped<global::TestApp.BackgroundWorker, global::TestApp.BackgroundWorker>"));
    }

    /// <summary>
    ///     One implementation among several: a TryAdd would keep whichever registered first and drop
    ///     this one, which is how a package's permission provider can register and never run.
    /// </summary>
    [Fact]
    public void Generator_WithMultiple_RegistersThroughTryAddEnumerable()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IContributor { }

                     [Service<IContributor>(Multiple = true)]
                     public class FirstContributor : IContributor { }

                     [Service(Multiple = true, Lifetime = Lifetime.Singleton)]
                     public class SecondContributor : IContributor { }
                     """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        output.Values.Should().Contain(s => s.Contains(
            "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped<global::TestApp.IContributor, global::TestApp.FirstContributor>());"));
        output.Values.Should().Contain(s => s.Contains(
            "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<global::TestApp.IContributor, global::TestApp.SecondContributor>());"));
    }

    [Fact]
    public void Generator_WithKeyedService_GeneratesKeyedRegistration()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IDatabase { }

                     [Service(Key = "primary")]
                     public class PrimaryDatabase : IDatabase { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // PRAG1646 is only emitted on .NET < 8 (IKeyedServiceProvider not available).
        // On .NET 8+, keyed services are natively supported — no warning expected.
        diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        output.Values.Should().Contain(s => s.Contains("primary"));
    }

    [Fact]
    public void Generator_WithDecorator_GeneratesDecoratorChain()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IUserService { }

                     [Service]
                     public class UserService : IUserService { }

                     [Decorator(Order = 1)]
                     public class CachingUserService : IUserService
                     {
                         public CachingUserService(IUserService inner) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Values.Should().Contain(s =>
            s.Contains("Decorate<global::TestApp.IUserService, global::TestApp.CachingUserService>"));
    }

    [Fact]
    public void Generator_WithNoServices_GeneratesNothing()
    {
        // Arrange
        var source = """
                     namespace TestApp;

                     public class RegularClass { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - Should not generate service registration file
        output.Should().NotContainKey("ServiceRegistrationExtensions.g.cs");
    }

    [Fact]
    public void Generator_WithOpenGenericClass_GeneratesOpenGenericRegistration()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IRepository<T> where T : class
                     {
                         T? GetById(int id);
                     }

                     [Service]
                     public class Repository<T> : IRepository<T> where T : class
                     {
                         public T? GetById(int id) => default;
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Values.Should().Contain(s =>
            s.Contains("AddScoped(typeof(global::TestApp.IRepository<>), typeof(global::TestApp.Repository<>))"));
    }

    [Fact]
    public void Generator_WithOpenGenericClass_SingletonLifetime_GeneratesSingletonRegistration()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface ICache<T> { }

                     [Service(Lifetime = Lifetime.Singleton)]
                     public class Cache<T> : ICache<T> { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Values.Should().Contain(s =>
            s.Contains("AddSingleton(typeof(global::TestApp.ICache<>), typeof(global::TestApp.Cache<>))"));
    }

    [Fact]
    public void Generator_WithMultipleTypeParameters_GeneratesCorrectRegistration()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IMapper<TSource, TTarget> { }

                     [Service]
                     public class Mapper<TSource, TTarget> : IMapper<TSource, TTarget> { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Values.Should().Contain(s =>
            s.Contains("AddScoped(typeof(global::TestApp.IMapper<,>), typeof(global::TestApp.Mapper<,>))"));
    }

    [Fact]
    public void Generator_WithAbstractClass_ReportsError()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Service]
                     public abstract class AbstractService { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().Contain(d => d.Id == "PRAG1645");
    }

    [Fact]
    public void Generator_WithMultipleServices_GeneratesAllRegistrations()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IServiceA { }
                     public interface IServiceB { }
                     public interface IServiceC { }

                     [Service]
                     public class ServiceA : IServiceA { }

                     [Service(Lifetime = Lifetime.Singleton)]
                     public class ServiceB : IServiceB { }

                     [Service(Lifetime = Lifetime.Transient)]
                     public class ServiceC : IServiceC { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        content.Should().Contain("AddScoped<global::TestApp.IServiceA, global::TestApp.ServiceA>");
        content.Should().Contain("AddSingleton<global::TestApp.IServiceB, global::TestApp.ServiceB>");
        content.Should().Contain("AddTransient<global::TestApp.IServiceC, global::TestApp.ServiceC>");
    }

    [Fact]
    public void Generator_WithGenericServiceAttribute_UsesExplicitInterface()
    {
        // Arrange - Class implements multiple interfaces, [Service<T>] specifies which one
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IPaymentProvider { }
                     public interface IDisposable { }

                     [Service<IPaymentProvider>]
                     public class StripeProvider : IPaymentProvider, IDisposable { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        // Should register as IPaymentProvider, not IDisposable
        content.Should().Contain("IPaymentProvider");
        content.Should().Contain("StripeProvider");
    }

    [Fact]
    public void Generator_WithGenericServiceAttribute_AndKey_GeneratesKeyedService()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IPaymentProvider { }

                     [Service<IPaymentProvider>(Key = "stripe")]
                     public class StripeProvider : IPaymentProvider { }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // PRAG1646 is only emitted on .NET < 8 (IKeyedServiceProvider not available).
        // On .NET 8+, keyed services are natively supported — no warning expected.
        diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        content.Should().Contain("stripe");
        content.Should().Contain("IPaymentProvider");
    }

    [Fact]
    public void Generator_WithMultipleDecorators_AppliesInOrder()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IUserService { }

                     [Service]
                     public class UserService : IUserService { }

                     [Decorator(Order = 2)]
                     public class LoggingUserService : IUserService
                     {
                         public LoggingUserService(IUserService inner) { }
                     }

                     [Decorator(Order = 1)]
                     public class CachingUserService : IUserService
                     {
                         public CachingUserService(IUserService inner) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        var content = output.Values.First(s => s.Contains("AddPragmaticServices"));
        // Decorators should be applied in order (1 before 2)
        var cachingIndex = content.IndexOf("CachingUserService", StringComparison.Ordinal);
        var loggingIndex = content.IndexOf("LoggingUserService", StringComparison.Ordinal);
        cachingIndex.Should().BeLessThan(loggingIndex, "Decorator with Order=1 should appear before Order=2");
    }
}