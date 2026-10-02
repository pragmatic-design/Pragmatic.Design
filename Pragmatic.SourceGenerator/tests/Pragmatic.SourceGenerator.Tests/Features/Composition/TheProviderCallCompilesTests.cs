using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator.Features.Composition.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     The provider call a generated database registration makes compiles against that provider.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The MySql one never had: no MySQL EF provider was referenced anywhere, and the generated
///         <c>UseMySql(connectionString)</c> is Pomelo's name, which stops at EF Core 9. On EF Core 10 the
///         provider is Oracle's <c>MySql.EntityFrameworkCore</c>, whose entry point is <c>UseMySQL</c>.
///     </para>
///     <para>
///         The line is compiled exactly as <see cref="DatabaseProviderCall" /> writes it, with the
///         provider's real assembly referenced — the three emitters that make the call all take it from
///         there.
///     </para>
/// </remarks>
public class TheProviderCallCompilesTests
{
    /// <summary>Every assembly the test process runs with: the BCL, EF Core and the two providers.</summary>
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    /// <summary>
    ///     The call as both emitters write it — on the host's <c>DbContextOptionsBuilder</c> and on the
    ///     entry point's <c>DbContextOptionsBuilder&lt;TContext&gt;</c> — with the connection string they pass,
    ///     <c>configuration["…"]</c>, which is a <c>string?</c>, in a file under <c>#nullable enable</c> like
    ///     every generated one. Errors, and nullable warnings: an application built with warnings as errors
    ///     stops on those too.
    /// </summary>
    private static string[] Problems(string provider, string? configKey)
    {
        var call = $"{DatabaseProviderCall.UseMethod(provider)}({Arguments(provider, configKey)})";
        var source = $$"""
            #nullable enable
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.Configuration;

            internal sealed class AppDbContext : DbContext { }

            internal static class Registration
            {
                internal static void Host(DbContextOptionsBuilder options, IConfiguration configuration)
                    => options.{{call}};

                internal static DbContextOptions<AppDbContext> Entry(IConfiguration configuration)
                    => new DbContextOptionsBuilder<AppDbContext>()
                        .{{call}}
                        .Options;
            }
            """;

        var compilation = CSharpCompilation.Create(
            "ProviderCall",
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id.StartsWith("CS86", StringComparison.Ordinal))
            .Select(d => d.ToString())
            .ToArray();
    }

    /// <summary>The arguments as the host template writes them, with or without a declared key.</summary>
    private static string Arguments(string provider, string? configKey)
        => configKey is null
            ? DatabaseProviderCall.Arguments(provider, null, null)
            : DatabaseProviderCall.Arguments(provider, $"configuration[\"{configKey}\"]", configKey);

    private const string ConfiguredKey = "ConnectionStrings:App";

    [Theory]
    [InlineData(ConfiguredKey)]
    [InlineData(null)]
    public void TheMySqlCall_Compiles(string? configKey)
        => Problems("MySql", configKey).Should().BeEmpty();

    /// <summary>
    ///     What the non-nullable parameter is given when the key has no value: a failure that names the key,
    ///     instead of the provider's own argument check, which cannot.
    /// </summary>
    [Fact]
    public void TheMySqlCall_NamesTheKey_WhenItHasNoValue()
        => Arguments("MySql", ConfiguredKey)
            .Should().Contain("?? throw new global::System.InvalidOperationException(\"The connection string 'ConnectionStrings:App' is not configured.\")");

    /// <summary>The retry every server provider gets, MySql included.</summary>
    [Fact]
    public void TheMySqlCall_RetriesOnFailure()
        => Arguments("MySql", ConfiguredKey)
            .Should().Contain("providerOptions => providerOptions.EnableRetryOnFailure()");

    /// <summary>The control: the PostgreSQL call compiled already, and still does.</summary>
    [Theory]
    [InlineData(ConfiguredKey)]
    [InlineData(null)]
    public void ThePostgreSqlCall_Compiles(string? configKey)
        => Problems("PostgreSql", configKey).Should().BeEmpty();

    /// <summary>
    ///     The control of the throw: a provider whose connection string accepts <see langword="null" /> is
    ///     passed the read unchanged — the entry's <c>DeclaredConnectionStrings.Require</c> names the key
    ///     for it.
    /// </summary>
    [Fact]
    public void ThePostgreSqlCall_PassesTheReadUnchanged()
        => Arguments("PostgreSql", ConfiguredKey)
            .Should().NotContain("throw");
}
