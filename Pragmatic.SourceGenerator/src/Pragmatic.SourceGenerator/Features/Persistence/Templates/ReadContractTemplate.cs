using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Emits a boundary read contract (#3): the <c>I{Module}Reads</c> interface, an implementation that runs
///     each published query via <c>IQueryExecutor</c> over the entity's <c>IReadRepository</c>, and a DI
///     registration. Other boundaries inject the interface to enforce cross-boundary invariants acyclically.
/// </summary>
internal sealed class ReadContractTemplate : CSharpTemplate
{
    private const string ReadRepository = "global::Pragmatic.Persistence.Repository.IReadRepository";
    private const string QueryExecutor = "global::Pragmatic.Persistence.Query.Executors.IQueryExecutor";

    private readonly string _contractName;
    private readonly string _implName;
    private readonly string _namespace;
    private readonly IReadOnlyList<PublishedQueryModel> _queries;

    public ReadContractTemplate(string contractName, string contractNamespace, IReadOnlyList<PublishedQueryModel> queries)
    {
        _contractName = contractName;
        _implName = ImplementationNameFor(contractName);
        _namespace = contractNamespace;
        _queries = queries;
    }

    /// <summary>The implementation name a contract name produces.</summary>
    /// <remarks>
    ///     Public because the metadata written for the host has to name the registration this template
    ///     emits, and two copies of a naming rule are two names as soon as one changes.
    /// </remarks>
    public static string ImplementationNameFor(string contractName)
        => contractName.StartsWith("I") ? contractName.Substring(1) : contractName + "Impl";

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput() => new($"_ReadContract.{_contractName}.g.cs", ToSourceText());

    protected override bool Validate() => _queries.Count > 0;

    /// <summary>Distinct entities across the contract's queries → one repository field each.</summary>
    private IReadOnlyList<(string EntityFqn, string IdFqn, string Field)> Repositories() =>
        _queries
            .GroupBy(q => q.EntityTypeFullName)
            .Select(g =>
            {
                var first = g.First();
                var field = $"_{TemplateHelpers.ToCamelCase(first.EntityShortName)}Repository";
                return (first.EntityTypeFullName, first.EntityIdTypeFullName, field);
            })
            .ToList();

    private string RepositoryFieldFor(PublishedQueryModel query) =>
        $"_{TemplateHelpers.ToCamelCase(query.EntityShortName)}Repository";

    /// <summary>
    ///     The queryable this query is executed over.
    /// </summary>
    /// <remarks>
    ///     The repository has carried the strategy switch — tracking, and whether the Pragmatic filter
    ///     pipeline runs — since it was first generated; what was missing was a caller that named a
    ///     strategy. A query that declares none still calls the parameterless overload, so the contract
    ///     emits exactly what it emitted before <c>[QueryStrategy]</c> was read.
    /// </remarks>
    private string QuerySourceFor(PublishedQueryModel query) =>
        query.Strategy is { } strategy
            ? $"{RepositoryFieldFor(query)}.Query({strategy.ToRuntimeConstant()})"
            : $"{RepositoryFieldFor(query)}.Query()";

    public override void RenderFile()
    {
        AppendNamespace(_namespace);
        AppendLine();

        RenderInterface();
        AppendLine();
        RenderImplementation();
        AppendLine();
        RenderRegistration();
    }

    private void RenderInterface()
    {
        XmlSummary($"Published read contract — inject this from another boundary to read, never the owning module.");

        // The consuming module's generator sees its own [Service] classes and nothing else, so without
        // this it reported the contract it was handed as unregistered (PRAG1641) and the application had
        // to register its own service by hand. Scoped is what Add{Contract} below calls.
        AppendLine("[global::Pragmatic.Composition.Attributes.ProvidedByHost("
                   + "global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]");
        AppendLine($"public interface {_contractName}");
        AppendLine("{");
        IncreaseIndent();
        foreach (var q in _queries)
            AppendLine($"global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<{q.ResultTypeFullName}>> {q.MethodName}({q.QueryTypeFullName} query, global::System.Threading.CancellationToken ct = default);");
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderImplementation()
    {
        var repos = Repositories();

        AppendLine($"public sealed class {_implName} : {_contractName}");
        AppendLine("{");
        IncreaseIndent();

        foreach (var (entity, id, field) in repos)
            AppendLine($"private readonly {ReadRepository}<{entity}> {field};");
        AppendLine($"private readonly {QueryExecutor} _queryExecutor;");
        AppendLine();

        // Constructor
        var ctorParams = repos
            .Select(r => $"{ReadRepository}<{r.EntityFqn}> {r.Field.TrimStart('_')}")
            .Append($"{QueryExecutor} queryExecutor");
        AppendLine($"public {_implName}({string.Join(", ", ctorParams)})");
        AppendLine("{");
        IncreaseIndent();
        foreach (var (_, _, field) in repos)
            AppendLine($"{field} = {field.TrimStart('_')};");
        AppendLine("_queryExecutor = queryExecutor;");
        DecreaseIndent();
        AppendLine("}");

        foreach (var q in _queries)
        {
            AppendLine();
            AppendLine($"public global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<{q.ResultTypeFullName}>> {q.MethodName}({q.QueryTypeFullName} query, global::System.Threading.CancellationToken ct = default)");
            AppendLine($"    => _queryExecutor.ExecuteAllAsync<{q.EntityTypeFullName}, {q.ResultTypeFullName}>(query, {QuerySourceFor(q)}, ct);");
        }

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderRegistration()
    {
        AppendLine($"public static class {_implName}RegistrationExtensions");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{_implName}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<{_contractName}, {_implName}>(services);");
        AppendLine("return services;");
        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
    }
}
