using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Identity;
using Pragmatic.Identity.Local;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>[PragmaticUser]</c> entity that owns a <c>LocalIdentity</c> gets its <c>ILocalIdentityStore</c>
///     generated and registered.
/// </summary>
/// <remarks>
///     Every application that signs its users in locally needs the same class: the same finders on the
///     owned identity, the same save. Hand-written copies drift — one normalizes the email and another
///     does not, and a copy that saves through the <c>DbContext</c> skips the unit of work. Nothing in
///     it depends on the application except whether a user may register themselves,
///     which the entity declares with <see cref="ISelfRegisteringUser{TSelf}" />.
/// </remarks>
public class TheLocalIdentityStoreIsGeneratedTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<PragmaticUserAttribute>(),
        GeneratorTestHelper.FromType<LocalIdentity>(),
        GeneratorTestHelper.FromType<IdentityRecord>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
        GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute>()
    ];

    /// <summary>
    ///     The errors in the store's own file. The harness references what the store needs, not every
    ///     package the rest of the generated output would.
    /// </summary>
    private static IEnumerable<string> StoreErrors(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("Member.LocalIdentityStore") == true)
            .Select(d => $"{d.Id}: {d.GetMessage()}");

    /// <summary>Marks the compilation as referencing Composition — what registers services at all.</summary>
    private const string CompositionStub = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        """;

    private static SourceGenRunResult Run(string user, string extra = "") =>
        GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(CompositionStub + $$"""

            namespace Contoso.Accounts
            {
                using Pragmatic.Actions.Attributes;
                using Pragmatic.Identity;
                using Pragmatic.Identity.Local;
                using Pragmatic.Persistence.Entity;

                [Boundary]
                public partial class AccountsBoundary;

                {{user}}

                {{extra}}
            }
            """, References);

    private const string UserWhoCannotRegister = """
        [Entity]
        [PragmaticUser(MatchClaim = "sub")]
        public partial class Member : IEntity
        {
            public string? DisplayName { get; set; }
            public LocalIdentity? Credentials { get; set; }
        }
        """;

    private const string UserWhoRegisters = """
        [Entity]
        [PragmaticUser(MatchClaim = "sub")]
        public partial class Member : IEntity, ISelfRegisteringUser<Member>
        {
            public string? DisplayName { get; set; }
            public LocalIdentity? Credentials { get; set; }

            public static Member Register(LocalIdentity identity)
            {
                var member = Create();
                member.DisplayName = identity.Email;
                member.Credentials = identity;
                return member;
            }
        }
        """;

    private static string? Store(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Member.LocalIdentityStore"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    private static string? Registration(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("_Infra.DI.ServiceRegistration"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    [Fact]
    public void AUserWithALocalIdentity_GetsAStore_ThatCompiles()
    {
        var result = Run(UserWhoCannotRegister);

        var store = Store(result);
        store.Should().NotBeNull();
        store!.Should().Contain("u => u.Credentials!.Email == email",
            "the finder reads the owned identity through the property the entity declares");
        store.Should().Contain("unitOfWork.SaveChangesAsync(ct)",
            "every save goes through the unit of work, like any other write of the boundary");
        StoreErrors(result).Should().BeEmpty();
    }

    [Fact]
    public void TheStore_IsRegisteredAsTheLocalIdentityStore()
    {
        Registration(Run(UserWhoCannotRegister)).Should().Contain(
            "services.TryAddScoped<global::Pragmatic.Identity.Local.Services.ILocalIdentityStore, "
            + "global::Contoso.Accounts.Member.LocalIdentityStore>();");
    }

    [Fact]
    public void AUserThatDoesNotRegisterItself_RefusesARegistration()
    {
        var store = Store(Run(UserWhoCannotRegister));

        store.Should().Contain("throw new global::System.NotSupportedException(");
    }

    [Fact]
    public void AUserThatRegistersItself_IsCreatedThroughItsDeclaration()
    {
        var result = Run(UserWhoRegisters);

        var store = Store(result);
        store.Should().Contain("users.Add(global::Contoso.Accounts.Member.Register(identity));");
        store.Should().NotContain("NotSupportedException");
        StoreErrors(result).Should().BeEmpty();
    }

    /// <summary>The application's own store wins: nothing is generated, so nothing competes with it.</summary>
    [Fact]
    public void AnApplicationThatWritesItsOwnStore_KeepsIt()
    {
        var result = Run(UserWhoCannotRegister, """
            public sealed class HandWrittenStore : Pragmatic.Identity.Local.Services.ILocalIdentityStore
            {
                public System.Threading.Tasks.ValueTask<LocalIdentity?> FindByEmailAsync(string email, System.Threading.CancellationToken ct = default) => default;
                public System.Threading.Tasks.ValueTask<LocalIdentity?> FindByExternalKeyAsync(string externalKey, System.Threading.CancellationToken ct = default) => default;
                public System.Threading.Tasks.ValueTask<LocalIdentity> CreateAsync(LocalIdentity identity, System.Threading.CancellationToken ct = default) => new(identity);
                public System.Threading.Tasks.ValueTask UpdateAsync(LocalIdentity identity, System.Threading.CancellationToken ct = default) => default;
                public System.Threading.Tasks.ValueTask<bool> EmailExistsAsync(string email, System.Threading.CancellationToken ct = default) => default;
            }
            """);

        Store(result).Should().BeNull();
        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values
            .Should().NotContain(source => source.Contains("Member.LocalIdentityStore"));
    }

    /// <summary>The control: a user entity without local credentials has nothing for a store to read.</summary>
    [Fact]
    public void AUserWithoutALocalIdentity_GetsNoStore()
    {
        var result = Run("""
            [Entity]
            [PragmaticUser(MatchClaim = "sub")]
            public partial class Member : IEntity
            {
                public string ExternalKey { get; set; } = "";
            }
            """);

        Store(result).Should().BeNull();
    }
}
