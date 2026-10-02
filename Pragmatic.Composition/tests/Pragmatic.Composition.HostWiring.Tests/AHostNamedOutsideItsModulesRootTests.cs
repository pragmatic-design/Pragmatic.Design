// Pragmatic.Composition.HostWiring.Tests - A host assembly named outside its modules' root

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     The host's generated code lives in a namespace named after its assembly; the registration of a
///     module's DbContext is declared in the module's root namespace. A host is free to be called anything
///     — <c>Api</c>, <c>App.Host</c> — and has to compile whatever it is called.
/// </summary>
/// <remarks>
///     Invisible until measured because every host in the repository is named <c>{Root}.Host</c>, where
///     the enclosing-namespace rule happens to reach an unqualified <c>Add{Boundary}DbContext</c>.
/// </remarks>
public sealed class AHostNamedOutsideItsModulesRootTests
{
    private const string TwoArity = "[Include<LeaveModule, AppDatabase>]";
    private const string ThreeArity = "[Include<LeaveModule, AppDatabase, ExplicitLeaveDbContext>]";

    private static string Host(string include) => $$"""
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using TimeOff.Leave;

        namespace TimeOff.Host;

        [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
        public sealed class AppDatabase : PragmaticDatabase;

        public sealed class ExplicitLeaveDbContext(DbContextOptions<ExplicitLeaveDbContext> options)
            : DbContext(options);

        [Module]
        {{include}}
        public sealed class TimeOffHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    private static IReadOnlyList<string> ErrorsOfAHostNamed(string hostAssemblyName, string include)
        => ModuleAndHost.Generate(
            "TimeOff.Leave",
            [AModuleWithoutEntitiesOnADatabaseTests.ModuleWithoutEntities, AModuleWithoutEntitiesOnADatabaseTests.OneEntity],
            hostAssemblyName,
            Host(include)).Errors;

    [Theory]
    [InlineData(TwoArity)]
    [InlineData(ThreeArity)]
    public void AHostAssemblyNamedOutsideTheModuleRoot_Compiles(string include)
        => ErrorsOfAHostNamed("App.Host", include).Should().BeEmpty();

    /// <summary>The control: the name every host in the repository has, which always compiled.</summary>
    [Theory]
    [InlineData(TwoArity)]
    [InlineData(ThreeArity)]
    public void AHostAssemblyNamedUnderTheModuleRoot_StillCompiles(string include)
        => ErrorsOfAHostNamed("TimeOff.Host", include).Should().BeEmpty();
}
