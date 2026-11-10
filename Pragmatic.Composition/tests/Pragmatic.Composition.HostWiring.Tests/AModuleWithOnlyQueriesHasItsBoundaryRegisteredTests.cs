// Pragmatic.Composition.HostWiring.Tests - A module whose operations are all queries

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A module whose operations are all queries has its boundary registered by the host (#634).
/// </summary>
/// <remarks>
///     <para>
///         The host called <c>Add{Module}Boundary</c> only for the assemblies with discovered actions, mutations or packages. Queries
///         are in neither list, so a read-only module — a directory, a reporting module — had <c>I{Module}Actions</c> generated with its
///         query members and its registration extension, and nothing called it: "No service for type 'IWorkforceActions'" the first
///         time another module injected it. The same shape a mutations-only module had before mutations were counted.
///     </para>
///     <para>
///         Measured where it fails: the registration the host generates is run and the interface resolved from the container it
///         builds. The control is a module with no operations, which has no boundary registration to call: the host must not name one.
///     </para>
/// </remarks>
public sealed class AModuleWithOnlyQueriesHasItsBoundaryRegisteredTests
{
    private const string Module = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;

        namespace App.Workforce;

        [Boundary]
        public partial class WorkforceBoundary;

        [Module(Name = "App.Workforce")]
        public sealed class WorkforceModule;
        """;

    private const string Entity = """
        using System;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace App.Workforce.Entities
        {
            [Entity]
            public partial class Employee : IEntity
            {
                public string Name { get; private set; } = "";
            }
        }

        namespace App.Workforce.Dtos
        {
            [MapFrom<App.Workforce.Entities.Employee>]
            [GenerateProjection]
            public partial class EmployeeDto
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = "";
            }
        }
        """;

    private const string OnlyQuery = """
        using Pragmatic.Persistence.Query.Attributes;
        using App.Workforce.Dtos;
        using App.Workforce.Entities;

        namespace App.Workforce.Queries;

        [Query<Employee, EmployeeDto>]
        public partial class SearchEmployeesQuery
        {
            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using App.Workforce;

        namespace App.Host;

        [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
        public sealed class AppDatabase : PragmaticDatabase;

        [Module]
        [Include<WorkforceModule, AppDatabase>]
        public sealed class AppHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    private const string BoundaryCall = "global::App.Workforce.WorkforceBoundaryExtensions.AddWorkforceBoundary(services);";

    [Fact]
    public void AModuleWithOnlyAQuery_HasItsBoundaryRegisteredByTheHost()
    {
        var (errors, generated) = ModuleAndHost.Generate("App.Workforce", [Module, Entity, OnlyQuery], "App.Host", Host);

        errors.Should().BeEmpty();
        string.Join("\n", generated.Values).Should().Contain(BoundaryCall,
            "the module generated I{Module}Actions and its registration, and nothing else registers them");
    }

    [Fact]
    public void AModuleWithOnlyAQuery_HasItsActionsInterfaceInTheHostsContainer()
    {
        var (errors, module, host) = ModuleAndHost.Emit("App.Workforce", [Module, Entity, OnlyQuery], "App.Host", Host);
        errors.Should().BeEmpty("the host has to compile, or there is no registration to run");

        var context = new AssemblyLoadContext("queries-only-boundary", isCollectible: true);
        try
        {
            var moduleAssembly = context.LoadFromStream(new MemoryStream(module));
            context.Resolving += (_, name) => name.Name == moduleAssembly.GetName().Name ? moduleAssembly : null;
            var hostAssembly = context.LoadFromStream(new MemoryStream(host!));

            var register = hostAssembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Single(m => m.Name == "RegisterAllDomainActions");

            var services = new ServiceCollection();
            register.Invoke(null, [services]);

            var actions = moduleAssembly.GetType("App.Workforce.IWorkforceActions", throwOnError: true)!;
            services.Should().Contain(d => d.ServiceType == actions,
                "another module injecting IWorkforceActions resolves it from this registration");

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService(actions).Should().NotBeNull(
                "'No service for type IWorkforceActions' is what the application measured");
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>The control: a module with no operations has no boundary registration, and the host names none.</summary>
    [Fact]
    public void AModuleWithNoOperations_HasNoBoundaryCall()
    {
        var (errors, generated) = ModuleAndHost.Generate("App.Workforce", [Module, Entity], "App.Host", Host);

        errors.Should().BeEmpty("a host naming a registration nobody generated would not compile");
        string.Join("\n", generated.Values).Should().NotContain("AddWorkforceBoundary");
    }
}
