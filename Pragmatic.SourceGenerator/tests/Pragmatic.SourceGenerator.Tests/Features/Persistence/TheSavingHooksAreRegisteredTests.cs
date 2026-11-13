using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Composition.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The host's EF Core registration wires the <c>OnSaving</c> hooks at the save (#644): the interceptor among the context's, and one
///     typed adapter per entity, which is how the interceptor finds an entity's hooks without reflection.
/// </summary>
/// <remarks>
///     What the interceptor does with them is measured in <c>TheSaveRunsTheOnSavingHooksTests</c> (EF Core, Sql, documents). This measures
///     that the generated host registers both, and that what it emits compiles.
/// </remarks>
public class TheSavingHooksAreRegisteredTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        .. ByName(
            "System.Text.Json",
            "System.ComponentModel.TypeConverter",
            "System.Linq",
            "System.Linq.Queryable",
            "System.Linq.Expressions",
            "System.ComponentModel.Annotations",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Caching",
            "Pragmatic.Actions"),
    ];

    // `global using System;`: the generated registration names Action<> as an SDK project's implicit usings allow.
    private const string Source = """
        global using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Contoso.Library;

        [Boundary]
        public partial class LibraryBoundary;

        [Entity]
        [BelongsTo<LibraryBoundary>]
        public partial class Book : IEntity { public string Title { get; set; } = ""; }

        public static class Program { public static void Main() { } }
        """;

    [Fact]
    public void TheContextRegistration_AddsTheInterceptor_AndAnAdapterPerEntity()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(Source, References);

        var registration = GeneratorTestHelper.GetGeneratedSource(result, "Persistence.DbContextRegistration");
        registration.Should().NotBeNull("the host has a boundary context to register, or the assertions below are about nothing");

        registration!.Should().Contain("new global::Pragmatic.Persistence.EFCore.Interceptors.EntitySavingHooksInterceptor(")
            .And.Contain("global::Pragmatic.Persistence.Lifecycle.EntitySavingHooks<global::Contoso.Library.Book>");

        GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("DbContextRegistration") == true)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .Should().BeEmpty();
    }

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));
}
