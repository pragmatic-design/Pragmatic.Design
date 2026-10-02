using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Caching;
using Pragmatic.Composition.Tests.Helpers;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Email;
using Pragmatic.Events;
using Pragmatic.FeatureFlags;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Resilience;
using Pragmatic.Result;
using Pragmatic.Storage;
using Pragmatic.Temporal.Clock;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Xunit;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     The DI validator knows which framework services the host provides, and which of them are
///     scoped. This suite checks it against the types themselves: an entry that matched no type would
///     report "not registered" for a service the host does register, and let a singleton capture a
///     scoped one without a warning.
/// </summary>
/// <remarks>
///     <para>
///         The answer comes from <c>[ProvidedByHost]</c> on each contract, in the package that
///         registers it, rather than from a list of names inside the generator. These theories hold
///         the declaration to everything such a list would carry, the captive-dependency check
///         included.
///     </para>
///     <para>
///         ⚠️ They include <c>IFileStorage</c>, <c>IEmailSender</c> and <c>ITenantStore</c>, which are
///         registered exactly like the names above them and are easy for a hand-kept list to miss:
///         without them, an application that uses storage or e-mail from a <c>[Service]</c> cannot
///         build. And a type nothing registers is reported rather than trusted: see
///         <see cref="AConcreteClassNothingRegisters_IsReported" />.
///     </para>
/// </remarks>
public class WellKnownFrameworkTypesTests
{
    /// <summary>What the source's own declarations need: the actions the invokers are closed over.</summary>
    private static readonly Type[] TheSourceNeeds = [typeof(DomainAction<>), typeof(Result<,>)];

    public static TheoryData<Type> ProvidedByTheFramework =>
    [
        typeof(IUnitOfWork),
        typeof(ICurrentUser),
        typeof(IClock),
        typeof(ICacheStack),
        typeof(IResiliencePipelineProvider),
        typeof(ITenantContext),
        typeof(IFeatureFlags),
        typeof(IDiscoveryService),
        typeof(IPermissionChecker),
        typeof(IUserAuthorization),
        typeof(IDomainEventDispatcher),
        typeof(IFileStorage),
        typeof(IEmailSender),
        typeof(ITenantStore),
        typeof(IRepository<>),
        typeof(IReadRepository<>),
        typeof(IDomainActionInvoker<,>),
        typeof(IVoidDomainActionInvoker<>),
        typeof(IQueryFilter<>),
        typeof(IQueryFilterToggle),
        typeof(IResourceAuthorizer<>),
        typeof(IValidator<>),
        typeof(IDefaultValueGenerator<,>)
    ];

    /// <remarks>
    ///     Seven of these were in the generator's scoped list; the rest are registered Scoped just the
    ///     same and were not, so a singleton capturing them went unreported. Moving the lifetime onto the
    ///     contract is what closed that — the registration and the declaration are now one edit apart.
    /// </remarks>
    public static TheoryData<Type> ScopedInTheHost =>
    [
        typeof(IUnitOfWork),
        typeof(ICurrentUser),
        typeof(ITenantContext),
        typeof(IFeatureFlags),
        typeof(IRepository<>),
        typeof(IReadRepository<>),
        typeof(IQueryFilter<>),
        typeof(IPermissionChecker),
        typeof(IUserAuthorization),
        typeof(IDomainEventDispatcher),
        typeof(IQueryFilterToggle),
        typeof(IResourceAuthorizer<>),
        typeof(IDomainActionInvoker<,>),
        typeof(IVoidDomainActionInvoker<>)
    ];

    [Theory]
    [MemberData(nameof(ProvidedByTheFramework))]
    public void AServiceInjectingIt_IsNotReportedAsUnregistered(Type dependency)
    {
        var (diagnostics, sourceErrors) = GeneratorTestHelper.RunGenerator(ServiceInjecting(dependency, "Scoped"), [dependency, .. TheSourceNeeds]);

        sourceErrors.Should().BeEmpty("the test names the real type, so it has to bind");
        diagnostics.Should().NotContain(d => d.Id == "PRAG1641");
    }

    [Theory]
    [MemberData(nameof(ScopedInTheHost))]
    public void ASingletonCapturingIt_IsReportedAsACaptiveDependency(Type dependency)
    {
        var (diagnostics, sourceErrors) = GeneratorTestHelper.RunGenerator(ServiceInjecting(dependency, "Singleton"), [dependency, .. TheSourceNeeds]);

        sourceErrors.Should().BeEmpty("the test names the real type, so it has to bind");
        diagnostics.Should().Contain(d => d.Id == "PRAG1642");
    }

    /// <summary>
    ///     <c>CachedPermissionResolver</c> was in the generator's list of types the host
    ///     provides, and nothing registers it: <c>UsePragmaticAuthorization</c> constructs it inside the
    ///     factory for <c>IUserAuthorization</c>, so asking the container for the class itself throws.
    ///     The list bought silence for a dependency that fails at the first request; the contract is what
    ///     a service injects, and it is the one that carries the declaration.
    /// </summary>
    [Fact]
    public void AConcreteClassNothingRegisters_IsReported()
    {
        var (diagnostics, sourceErrors) = GeneratorTestHelper.RunGenerator(
            ServiceInjecting(typeof(CachedPermissionResolver), "Scoped"),
            [typeof(CachedPermissionResolver), .. TheSourceNeeds]);

        sourceErrors.Should().BeEmpty("the test names the real type, so it has to bind");
        diagnostics.Should().Contain(d => d.Id == "PRAG1641",
            "nothing registers the class — the resolvable contract is IUserAuthorization");
    }

    /// <summary>The control: a singleton holding a framework singleton is not a captive dependency.</summary>
    [Fact]
    public void ASingletonCapturingAFrameworkSingleton_IsNotReported()
    {
        var (diagnostics, sourceErrors) = GeneratorTestHelper.RunGenerator(ServiceInjecting(typeof(IClock), "Singleton"), [typeof(IClock), .. TheSourceNeeds]);

        sourceErrors.Should().BeEmpty("the test names the real type, so it has to bind");
        diagnostics.Should().NotContain(d => d.Id == "PRAG1642");
        diagnostics.Should().NotContain(d => d.Id == "PRAG1641");
    }

    private static string ServiceInjecting(Type dependency, string lifetime) => $$"""
        using Pragmatic.Composition.Attributes;

        namespace TestApp;

        public interface IMyService { }

        public class Thing : global::Pragmatic.Persistence.Entity.IEntity
        {
            public System.Guid PersistenceId => default;
        }

        public abstract class SomeAction : global::Pragmatic.Actions.Abstractions.DomainAction<int> { }
        public abstract class SomeVoidAction : global::Pragmatic.Actions.Abstractions.VoidDomainAction { }

        [Service(Lifetime = Lifetime.{{lifetime}})]
        public class MyService : IMyService
        {
            public MyService({{NameOf(dependency)}} dependency) { }
        }
        """;

    /// <summary>
    ///     A closed, fully qualified name. An open generic is closed over <c>Thing</c>, an entity class, which
    ///     satisfies the constraints most of these types declare (<c>class</c>, <c>IEntity</c>); the invokers
    ///     are closed over an action, the one thing their constraint accepts.
    /// </summary>
    private static string NameOf(Type type)
    {
        if (!type.IsGenericTypeDefinition)
            return "global::" + type.FullName;

        var name = type.FullName!;
        var arguments = type == typeof(IDomainActionInvoker<,>) ? "global::TestApp.SomeAction, int"
            : type == typeof(IVoidDomainActionInvoker<>) ? "global::TestApp.SomeVoidAction"
            : string.Join(", ", Enumerable.Repeat("global::TestApp.Thing", type.GetGenericArguments().Length));
        return $"global::{name[..name.IndexOf('`')]}<{arguments}>";
    }
}
