using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[FromCurrentUser(member)]</c> on a query of a module that does not declare the user: the <c>[PragmaticUser]</c> entity is in a
///     module it references, and the invoker reads it through that module's resolver.
/// </summary>
/// <remarks>
///     "My tasks" lives with the tasks, and the user with the accounts. Reading the user entity only from the query's own compilation
///     answered "there is no [PragmaticUser] entity in this compilation" to every such query, and the application turned them into
///     actions that looked the caller up by hand.
/// </remarks>
public class AQueryBoundToAUserOfAnotherModuleTests
{
    /// <summary>Module A: the user, persisted with EF Core, so A writes its resolver.</summary>
    private const string Accounts = """
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Identity;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace Pragmatic.Identity.Persistence.Entities
        {
            public class RolePermission { }
        }

        namespace Contoso.Accounts
        {
            [Boundary]
            public partial class AccountsBoundary { }

            [Entity]
            [BelongsTo<AccountsBoundary>]
            [PragmaticUser]
            public partial class AppUser : IEntity
            {
                public string ExternalIdentityKey { get; set; } = "";
                public string Email { get; set; } = "";
            }

            [PragmaticDbContext("Accounts")]
            public partial class AccountsDbContext { }
        }
        """;

    /// <summary>Module B: the tasks, and the query that reads the caller's.</summary>
    private const string Tasks = """
        using System;
        using Contoso.Accounts;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Identity;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Contoso.Work
        {
            [Boundary]
            public partial class WorkBoundary { }

            [Entity]
            [BelongsTo<WorkBoundary>]
            public partial class WorkItem : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public Guid AssigneeId { get; set; }
            }

            [PragmaticDbContext("Work")]
            public partial class WorkDbContext { }

            [Query<WorkItem, WorkItem>]
            [Endpoint(HttpVerb.Get, "api/my-work")]
            public partial class MyWorkQuery
            {
                [FromCurrentUser(nameof(AppUser.Id))]
                [Filter]
                public Guid AssigneeId { get; private set; }
            }
        }
        """;

    private static readonly MetadataReference AccountsModule = BuildAccounts();

    [Fact]
    public void TheMember_IsBound_ThroughTheReferencedModulesResolver()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Tasks, [AccountsModule]);

        diagnostics.Where(d => d.Id == "PRAG0731").Select(d => d.GetMessage()).Should().BeEmpty();

        var invoker = Invoker(sources);
        invoker.Should().Contain("new global::Contoso.Accounts.AppUserResolver(",
            "the resolver Accounts wrote is the one that reads its user");
        invoker.Should().Contain("IUserLookup<global::Contoso.Accounts.AppUser>");
        invoker.Should().Contain("q.AssigneeId = __user.Id;");
    }

    [Fact]
    public void TheQueryModule_Compiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Tasks,
            static path => path.EndsWith(".QueryInvoker.g.cs") || path.EndsWith("TestSource.cs"),
            [AccountsModule]);

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    /// <summary>
    ///     The control: the user's own module writes the resolver, and the referencing module writes none — a second resolver, and its
    ///     registration, would be a second service for the same user.
    /// </summary>
    [Fact]
    public void TheQueryModule_WritesNoResolverOfItsOwn()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Tasks, [AccountsModule]);

        sources.Keys.Should().NotContain(k => k.Contains("AppUser.Resolver"));
        sources.Values.Should().NotContain(s => s.Contains("\"implementation\": \"global::Contoso.Accounts.AppUserResolver\""));
    }

    /// <summary>The control: without the module that declares it there is still no user, and that is still said.</summary>
    [Fact]
    public void WithoutTheUsersModule_TheBindingIsStillReported()
    {
        var withoutAccounts = Tasks
            .Replace("using Contoso.Accounts;", "")
            .Replace("nameof(AppUser.Id)", "\"Id\"");

        var (_, diagnostics) = TraitCompilationHarness.Generate(withoutAccounts);

        diagnostics.Where(d => d.Id == "PRAG0731").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("no [PragmaticUser] entity"));
    }

    private static MetadataReference BuildAccounts()
    {
        var (reference, errors) = TraitCompilationHarness.Build(Accounts, "Contoso.Accounts");
        reference.Should().NotBeNull("the user's module has to build: " + TraitCompilationHarness.FormatErrors(errors));
        return reference!;
    }

    private static string Invoker(Dictionary<string, string> sources)
        => sources.Single(pair => pair.Key.EndsWith("MyWorkQuery.QueryInvoker.g.cs")).Value;
}
