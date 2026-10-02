using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Emits the nested <c>Invoker</c> of a declared query: the one way to run it, over HTTP or in
///     process.
/// </summary>
/// <remarks>
///     <para>
///         The base — <c>QueryInvoker&lt;TQuery&gt;</c> — validates the input and checks the permission;
///         what this writes is the read, and the permission the author declared. Everything in it is
///         known at compile time, which is why none of it is looked up at run time: the executor
///         overload is chosen by the query's shape, the context by its boundary, the permission by the
///         attribute.
///     </para>
///     <para>
///         ⚠️ <b>The source is the raw set.</b> <c>GetRequiredKeyedService&lt;DbContext&gt;</c> then
///         <c>Set&lt;TEntity&gt;()</c>, exactly as the generated endpoint has always done, and never
///         <c>repository.Query()</c>: that is already <c>ApplyFilters(Set)</c>, and <c>ApplyFilters</c>
///         calls <c>IgnoreQueryFilters</c>, of which EF keeps the <em>last</em> in the chain. Handing an
///         already-filtered source to the executor would change which filters are active rather than
///         repeat them.
///     </para>
///     <para>
///         ⚠️ <b>The permission is a value here, not a registry lookup.</b>
///         <c>IPermissionRequirementRegistry</c> is built from an assembly's actions and mutations and
///         has never carried a query, so an invoker asking it would be told nothing is required and
///         would admit every caller — a permission step indistinguishable from no step at all.
///     </para>
/// </remarks>
internal sealed partial class QueryInvokerTemplate : CSharpTemplate
{
    private readonly QueryModel _model;

    public QueryInvokerTemplate(QueryModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Query] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "QueryInvoker", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsValid && _model.IsPartial;

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        var accessibility = AccessibilityHelper.Parse(_model.Accessibility);
        var mods = new ClassModifiers { Partial = true };

        if (_model.IsRecord)
            Record(_model.TypeName, RenderNestedInvoker, parameters: null,
                accessModifier: accessibility, modifiers: mods);
        else
            Class(_model.TypeName, RenderNestedInvoker,
                accessModifier: accessibility, modifiers: mods);
    }

    private void RenderNestedInvoker()
    {
        XmlSummary($"The pipeline every caller of <see cref=\"{_model.TypeName}\"/> goes through: "
                   + "validation, permission, then the read.");

        Class("Invoker", RenderInvokerBody,
            baseType: $"global::Pragmatic.Actions.Invoker.QueryInvoker<{QueryType}>",
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderInvokerBody()
    {
        AppendLine("private readonly global::System.IServiceProvider _services;");
        AppendLine();

        AppendLine($"public Invoker(global::System.IServiceProvider services) : base(services)");
        IncreaseIndent();
        AppendLine("=> _services = services;");
        DecreaseIndent();
        AppendLine();

        RenderPermission();
        RenderRun();
    }

    /// <summary>The permission the author declared, as the base asks for it.</summary>
    private void RenderPermission()
    {
        if (_model.RequiredPermissions.Count == 0)
            return;

        // CSharpLiteral escapes; the quotes are the caller's, as everywhere else it is used.
        var literals = string.Join(", ",
            _model.RequiredPermissions.Select(p => "\"" + StringHelper.CSharpLiteral(p) + "\""));

        AppendLine($"protected override string[]? RequiredPermissions => [{literals}];");

        // Only when it differs from the base's default, so the generated file says what is unusual
        // rather than restating what is already true.
        if (!_model.RequiresAllPermissions)
            AppendLine("protected override bool RequiresAllPermissions => false;");

        AppendLine();
    }

    private void RenderRun()
    {
        var entity = $"global::{_model.EntityTypeFullName}";
        var result = $"global::{_model.ResultTypeFullName}";
        var answer = _model.AnswerTypeFullName;

        XmlSummary("Runs the query: validated, permitted, then read.");
        AppendLine($"public global::System.Threading.Tasks.Task<global::Pragmatic.Result.Result<{answer}, "
                   + $"global::Pragmatic.Result.IError>> RunAsync(");
        IncreaseIndent();
        AppendLine($"{QueryType} query,");
        AppendLine("global::System.Threading.CancellationToken cancellationToken = default)");
        DecreaseIndent();
        AppendLine("=> RunAsync(query, async q =>");
        AppendLine("{");
        IncreaseIndent();

        // Inside the read because that is where the base's order puts it: validation and the permission
        // check have run, and nothing has been read. The query is the instance the caller passed, so the
        // values are on it when the executor builds the cache key and applies the filter.
        var failure = $"global::Pragmatic.Result.Result<{answer}, global::Pragmatic.Result.IError>.Failure";
        foreach (var statement in InvokerBindingEmitter.Statements(
                     _model.CurrentUserBindings, _model.ClockBindings, "q", "_services", "cancellationToken",
                     error => $"return {failure}({error});"))
            AppendLine(statement);

        AppendLine("var executor = global::Microsoft.Extensions.DependencyInjection"
                   + ".ServiceProviderServiceExtensions.GetRequiredService<"
                   + "global::Pragmatic.Persistence.Query.Executors.IQueryExecutor>(_services);");

        AppendLine(_model.BoundaryTypeName is not null
            ? "var dbContext = global::Microsoft.Extensions.DependencyInjection"
              + ".ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService<"
              + $"global::Microsoft.EntityFrameworkCore.DbContext>(_services, typeof({Boundary}));"
            : "var dbContext = global::Microsoft.Extensions.DependencyInjection"
              + ".ServiceProviderServiceExtensions.GetRequiredService<"
              + "global::Microsoft.EntityFrameworkCore.DbContext>(_services);");

        AppendLine($"var source = {Source(entity)};");
        AppendLine();

        if (_model.IsSingle)
        {
            AppendLine($"var answer = await executor.ExecuteSingleAsync<{entity}, {result}>("
                       + "q, source, cancellationToken).ConfigureAwait(false);");
            AppendLine();
            // Result<T> already carries the failure; unwrapping keeps the handler's answer identical to
            // the one it produced when it called the executor itself.
            AppendLine($"return answer.IsFailure && answer.Error is not null");
            IncreaseIndent();
            AppendLine($"? global::Pragmatic.Result.Result<{answer}, global::Pragmatic.Result.IError>"
                       + ".Failure(answer.Error)");
            AppendLine($": global::Pragmatic.Result.Result<{answer}, global::Pragmatic.Result.IError>"
                       + ".Success(answer.Value!);");
            DecreaseIndent();
        }
        else if (_model.IsPaged)
        {
            AppendLine($"var answer = await executor.ExecuteAsync<{entity}, {result}>("
                       + "q, source, cancellationToken).ConfigureAwait(false);");
            AppendLine();
            // The page is handed back whole, failure included: the endpoint answers 400 from it, and
            // an in-process caller reads Items and Error the same way it always has.
            AppendLine($"return global::Pragmatic.Result.Result<{answer}, global::Pragmatic.Result.IError>"
                       + ".Success(answer);");
        }
        else
        {
            AppendLine($"var answer = await executor.ExecuteAllAsync<{entity}, {result}>("
                       + "q, source, cancellationToken).ConfigureAwait(false);");
            AppendLine();
            AppendLine($"return global::Pragmatic.Result.Result<{answer}, global::Pragmatic.Result.IError>"
                       + ".Success(answer);");
        }

        DecreaseIndent();
        AppendLine("}, cancellationToken);");
    }

    /// <summary>What the query answers: one row, a page, or a list.</summary>
    private string QueryType => $"global::{_model.Namespace}.{_model.TypeName}";

    private string Boundary => _model.BoundaryTypeName!.StartsWith("global::")
        ? _model.BoundaryTypeName!
        : $"global::{_model.BoundaryTypeName}";

    /// <summary>
    ///     The queryable the executor is handed: the <c>DbSet</c>, narrowed by what the query declared.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Moved here with the read itself, from the generated handler. <b>Tracking:</b>
    ///         <c>Projection</c> and <c>Raw</c> both say nothing read this way will be written, so the
    ///         rows do not belong in the change tracker. <b>Filters:</b> only <c>Raw</c> lifts them,
    ///         because "no filters" is its definition, and it drops tenant isolation and soft delete
    ///         along with everything else.
    ///     </para>
    ///     <para>
    ///         ⚠️ Gathered into one call: EF keeps the <em>last</em> <c>IgnoreQueryFilters</c> in a chain
    ///         rather than accumulating them.
    ///     </para>
    /// </remarks>
    private string Source(string entity)
    {
        var source = $"dbContext.Set<{entity}>()";

        if (_model.Strategy is QueryStrategyKind.Projection or QueryStrategyKind.Raw)
            source = $"global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking({source})";

        if (_model.HasFilterOverrides || _model.Strategy is QueryStrategyKind.Raw)
            source = $"global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters({source})";

        return source;
    }
}
