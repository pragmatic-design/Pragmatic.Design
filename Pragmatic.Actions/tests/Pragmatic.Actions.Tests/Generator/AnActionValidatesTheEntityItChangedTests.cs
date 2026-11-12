using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Result;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A <c>[DomainAction]</c> validates the field rules (<c>[MaxLength]</c>, <c>[Required]</c>, …) of an entity it loaded and changed, after
///     the body and before the write, as the mutation path validates the entity it writes (#628).
/// </summary>
/// <remarks>
///     <para>
///         The generated action invoker checked the loaded entity's <c>[Invariant]</c> rules and none of its field rules, so an action that
///         set a field wrote a value no attribute was asked of.
///     </para>
///     <para>
///         Measured on the generated invoker itself: the module is compiled, loaded and invoked over one row, through a repository that
///         answers it and a unit of work that counts its saves and says whether it sees the row as modified.
///     </para>
/// </remarks>
public class AnActionValidatesTheEntityItChangedTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Interfaces;
        using Pragmatic.Persistence.Query.Results;
        using Pragmatic.Persistence.Repository;
        using Pragmatic.Result;
        using Pragmatic.Specification;
        using Pragmatic.Validation.Attributes;

        namespace TestApp.Catalog;

        [Boundary]
        public partial class CatalogBoundary;

        public partial class Product : IEntity
        {
            public Guid PersistenceId { get; set; }

            [MaxLength(5)]
            public string Code { get; set; } = "";
        }

        [DomainAction]
        [BelongsTo<CatalogBoundary>]
        [LoadEntity<Product>(nameof(ProductId))]
        public partial class RecodeProductAction : DomainAction<string>
        {
            public required Guid ProductId { get; init; }
            public string? NewCode { get; init; }

            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            {
                if (NewCode is not null)
                    _product.Code = NewCode;
                return Task.FromResult<Result<string, IError>>(_product.Code);
            }
        }

        public sealed class Rows(Product row) : IReadRepository<Product>
        {
            public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Product?>(row);
            public Task<List<Product>> FindAsync(ISpecification<Product> spec, CancellationToken ct = default) => Task.FromResult(new List<Product> { row });
            public Task<int> CountAsync(ISpecification<Product> spec, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<bool> ExistsAsync(ISpecification<Product> spec, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<Product?> FirstOrDefaultAsync(ISpecification<Product> spec, CancellationToken ct = default) => Task.FromResult<Product?>(row);
            public IQueryable<Product> Query() => throw new NotSupportedException();
            public Task<IReadOnlyList<TResult>> RunAsync<TResult>(IQuery<Product, TResult> query, CancellationToken ct = default)
                where TResult : class => throw new NotSupportedException();
            public Task<PagedResult<TResult>> RunAsync<TResult>(IPagedQuery<Product, TResult> query, CancellationToken ct = default)
                where TResult : class => throw new NotSupportedException();
        }

        /// <summary>Counts its saves, and sees the row as modified when its code is not the one it was read with.</summary>
        public sealed class Store(Product row) : IUnitOfWork
        {
            private readonly string _read = row.Code;
            public int Saves { get; private set; }
            public bool IsModified(object entity) => ReferenceEquals(entity, row) && row.Code != _read;
            public Task<int> SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.FromResult(1); }
            public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default) => throw new NotSupportedException();
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        public static class Harness
        {
            /// <summary>A row read with <paramref name="code" />, recoded to <paramref name="newCode" /> (null: left as it is) through the generated invoker.</summary>
            public static async Task<(IError? Error, int Saves)> RecodeAsync(IServiceProvider services, string code, string? newCode)
            {
                var row = new Product { PersistenceId = Guid.NewGuid(), Code = code };
                var store = new Store(row);
                var invoker = new RecodeProductAction.Invoker(store, new Rows(row), services);
                var result = await invoker.InvokeAsync(new RecodeProductAction { ProductId = row.PersistenceId, NewCode = newCode });
                return (result.IsFailure ? result.Error : null, store.Saves);
            }
        }
        """;

    [Fact]
    public async Task ACodeLongerThanItsRule_IsRefused_AndNothingIsWritten()
    {
        var (error, saves) = await RecodeAsync("AB", "0123456789");

        error.Should().BeOfType<ValidationError>();
        saves.Should().Be(0, "the rule is asked before the write");
    }

    /// <summary>The control: a change that keeps the rule is written, so the check refuses only what it should.</summary>
    [Fact]
    public async Task ACodeWithinItsRule_IsWritten()
    {
        var (error, saves) = await RecodeAsync("AB", "ABCD");

        error.Should().BeNull();
        saves.Should().Be(1);
    }

    /// <summary>
    ///     A row whose stored value already breaks the rule, loaded and not changed: the unit of work does not see it as modified, so the
    ///     action is not refused for a value it did not write.
    /// </summary>
    [Fact]
    public async Task ARowTheActionDidNotChange_IsNotValidated()
    {
        var (error, saves) = await RecodeAsync("0123456789", null);

        error.Should().BeNull();
        saves.Should().Be(1);
    }

    private static SourceGenRunResult RunGenerator()
        => GeneratorTestHelper.RunGenerator<Pragmatic.SourceGenerator.PragmaticSourceGenerator>(Source,
        [
            .. GetActionsAndEntityReferences(),
            GeneratorTestHelper.FromTypeAssembly(typeof(ISyncValidator)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Validation.Attributes.MaxLengthAttribute))
        ]);

    private static async Task<(IError? Error, int Saves)> RecodeAsync(string code, string? newCode)
    {
        var result = RunGenerator();
        GetCompilationErrors(result).Select(e => e.ToString()).Should().BeEmpty();

        using var image = new MemoryStream();
        var emitted = result.OutputCompilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join("\n", emitted.Diagnostics));

        var loadContext = new AssemblyLoadContext("action-validates-what-it-changed", isCollectible: true);
        try
        {
            image.Position = 0;
            var harness = loadContext.LoadFromStream(image).GetType("TestApp.Catalog.Harness", throwOnError: true)!;

            var services = new ServiceCollection();
            services.AddSingleton(Options.Create(new ValidationOptions()));
            using var provider = services.BuildServiceProvider();

            var run = (Task<(IError?, int)>)harness.GetMethod("RecodeAsync", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [provider, code, newCode])!;
            return await run.ConfigureAwait(false);
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
