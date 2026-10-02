// Pragmatic.Composition.Tests - Validation Tests

using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Tests.Helpers;
using Xunit;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Tests for DI validation: circular dependencies, lifetime mismatches, missing registrations.
/// </summary>
public class ValidationTests
{
    [Fact]
    public void CircularDependency_DirectCycle_ReportsError()
    {
        // Arrange - A depends on B, B depends on A
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IServiceA { }
                     public interface IServiceB { }

                     [Service]
                     public class ServiceA : IServiceA
                     {
                         public ServiceA(IServiceB b) { }
                     }

                     [Service]
                     public class ServiceB : IServiceB
                     {
                         public ServiceB(IServiceA a) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().Contain(d => d.Id == "PRAG1602");
    }

    [Fact]
    public void CircularDependency_IndirectCycle_ReportsError()
    {
        // Arrange - A -> B -> C -> A
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IServiceA { }
                     public interface IServiceB { }
                     public interface IServiceC { }

                     [Service]
                     public class ServiceA : IServiceA
                     {
                         public ServiceA(IServiceB b) { }
                     }

                     [Service]
                     public class ServiceB : IServiceB
                     {
                         public ServiceB(IServiceC c) { }
                     }

                     [Service]
                     public class ServiceC : IServiceC
                     {
                         public ServiceC(IServiceA a) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.Should().Contain(d => d.Id == "PRAG1602");
    }

    [Fact]
    public void LifetimeMismatch_SingletonDependsOnScoped_ReportsWarning()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IScopedService { }
                     public interface ISingletonService { }

                     [Service(Lifetime = Lifetime.Scoped)]
                     public class ScopedService : IScopedService { }

                     [Service(Lifetime = Lifetime.Singleton)]
                     public class SingletonService : ISingletonService
                     {
                         public SingletonService(IScopedService scoped) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - PRAG1642: Lifetime mismatch warning
        diagnostics.Should().Contain(d => d.Id == "PRAG1642");
    }

    [Fact]
    public void LifetimeMismatch_SingletonDependsOnTransient_ReportsWarning()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface ITransientService { }
                     public interface ISingletonService { }

                     [Service(Lifetime = Lifetime.Transient)]
                     public class TransientService : ITransientService { }

                     [Service(Lifetime = Lifetime.Singleton)]
                     public class SingletonService : ISingletonService
                     {
                         public SingletonService(ITransientService transient) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - PRAG1642: Lifetime mismatch warning
        diagnostics.Should().Contain(d => d.Id == "PRAG1642");
    }

    [Fact]
    public void LifetimeMismatch_SingletonDependsOnSingleton_NoWarning()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface ISingletonA { }
                     public interface ISingletonB { }

                     [Service(Lifetime = Lifetime.Singleton)]
                     public class SingletonA : ISingletonA { }

                     [Service(Lifetime = Lifetime.Singleton)]
                     public class SingletonB : ISingletonB
                     {
                         public SingletonB(ISingletonA a) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - No PRAG1642 warning
        diagnostics.Should().NotContain(d => d.Id == "PRAG1642");
    }

    [Fact]
    public void Decorator_MissingInnerServiceParameter_ReportsError()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IUserService { }

                     [Service]
                     public class UserService : IUserService { }

                     [Decorator]
                     public class CachingUserService : IUserService
                     {
                         // Missing IUserService parameter in constructor
                         public CachingUserService() { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - PRAG1661: Decorator missing inner service
        diagnostics.Should().Contain(d => d.Id == "PRAG1661");
    }

    [Fact]
    public void Decorator_WithInnerService_NoError()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IUserService { }

                     [Service]
                     public class UserService : IUserService { }

                     [Decorator]
                     public class CachingUserService : IUserService
                     {
                         public CachingUserService(IUserService inner) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - No PRAG1661 error
        diagnostics.Should().NotContain(d => d.Id == "PRAG1661");
    }

    // The decorated interface is the one the constructor injects, not the first one declared. Inferred
    // from declaration order, PRAG1661 would tell a decorator implementing two interfaces whose
    // constructor takes the second that it "must have a constructor parameter of the decorated interface
    // type" — for the parameter it does have. The constructor is the stronger signal: a decorator takes
    // what it wraps.
    [Fact]
    public void Decorator_ImplementingTwoInterfaces_TakesTheOneItsConstructorInjects()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IDisposableMarker { }
                     public interface IUserService { }

                     [Service]
                     public class UserService : IUserService { }

                     [Decorator]
                     public class CachingUserService : IDisposableMarker, IUserService
                     {
                         public CachingUserService(IUserService inner) { }
                     }
                     """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Id == "PRAG1661",
            "the constructor names the decorated interface, whichever position it holds");
    }

    [Fact]
    public void StartupStep_NotImplementingInterface_ReportsPrag1630()
    {
        // A [StartupStep] class that forgets IStartupStep can be neither registered nor run, so it
        // must surface PRAG1630 instead of being silently dropped.
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [StartupStep]
                     public class MisconfiguredStep { }
                     """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().Contain(d => d.Id == "PRAG1630");
    }

    [Fact]
    public void Decorator_NoInterface_ReportsPrag1660()
    {
        // A [Decorator] with no interface decorates nothing: it is reported, not silently dropped.
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [Decorator]
                     public class DecoratesNothing { }
                     """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().Contain(d => d.Id == "PRAG1660");
    }

    [Fact]
    public void DependencyNotRegistered_ReportsWarning()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IUnregisteredService { }
                     public interface IMyService { }

                     [Service]
                     public class MyService : IMyService
                     {
                         public MyService(IUnregisteredService unregistered) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - PRAG1641: Dependency not registered
        diagnostics.Should().Contain(d => d.Id == "PRAG1641");
    }

    /// <summary>
    ///     A contract the host registers at runtime (<c>[ProvidedByHost]</c>) is not
    ///     "not registered": the module's generator cannot see the registration, and the contract says so.
    ///     The control is <see cref="DependencyNotRegistered_ReportsWarning" />.
    /// </summary>
    [Fact]
    public void ADependencyTheHostProvides_IsNotReportedAsUnregistered()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [ProvidedByHost]
                     public interface ITokenIssuer { }
                     public interface IMyService { }

                     [Service]
                     public class MyService : IMyService
                     {
                         public MyService(ITokenIssuer issuer) { }
                     }
                     """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Id == "PRAG1641");
    }

    /// <summary>The same through property injection, which the validator reads through a model of its own.</summary>
    [Fact]
    public void ADependencyTheHostProvides_ThroughAnInjectedProperty_IsNotReportedAsUnregistered()
    {
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     [ProvidedByHost]
                     public interface ITokenIssuer { }
                     public interface IMyService { }

                     [Service]
                     public class MyService : IMyService
                     {
                         [Inject(Required = true)]
                         public ITokenIssuer Issuer { get; set; } = null!;
                     }
                     """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Id == "PRAG1641");
    }

    [Fact]
    public void DependencyRegistered_NoWarning()
    {
        // Arrange
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IRegisteredService { }
                     public interface IMyService { }

                     [Service]
                     public class RegisteredService : IRegisteredService { }

                     [Service]
                     public class MyService : IMyService
                     {
                         public MyService(IRegisteredService registered) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - No PRAG1641 warning
        diagnostics.Should().NotContain(d => d.Id == "PRAG1641");
    }

    [Fact]
    public void WellKnownTypes_NotReportedAsUnregistered()
    {
        // Arrange - IServiceProvider is a well-known type
        var source = """
                     using System;
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IMyService { }

                     [Service]
                     public class MyService : IMyService
                     {
                         public MyService(IServiceProvider serviceProvider) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - No PRAG1641 warning for IServiceProvider
        diagnostics.Should().NotContain(d => d.Id == "PRAG1641");
    }

    [Fact]
    public void AbstractClass_AsService_ReportsError()
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

        // Assert - PRAG1645: Abstract class cannot be service
        diagnostics.Should().Contain(d => d.Id == "PRAG1645");
    }

    [Fact]
    public void NoCircularDependency_WithChain_NoError()
    {
        // Arrange - A -> B -> C (no cycle)
        var source = """
                     using Pragmatic.Composition.Attributes;

                     namespace TestApp;

                     public interface IServiceA { }
                     public interface IServiceB { }
                     public interface IServiceC { }

                     [Service]
                     public class ServiceC : IServiceC { }

                     [Service]
                     public class ServiceB : IServiceB
                     {
                         public ServiceB(IServiceC c) { }
                     }

                     [Service]
                     public class ServiceA : IServiceA
                     {
                         public ServiceA(IServiceB b) { }
                     }
                     """;

        // Act
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assert - No circular dependency error
        diagnostics.Should().NotContain(d => d.Id == "PRAG1602");
    }
}