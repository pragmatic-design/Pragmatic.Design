using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     The shape of the preparation hook both operation invokers override — <c>PrepareActionAsync</c>,
///     <c>PrepareMutationAsync</c> — which the base calls after validation and authorization and before
///     the body.
/// </summary>
/// <remarks>
///     It writes the bound properties, then — inside the filter scopes the operation declares — loads the
///     signed-in user's entity and the <c>[LoadEntity]</c> entities. Only the loads await: a hook that
///     writes the clock alone is not <c>async</c>, since an <c>async</c> method with no <c>await</c> is a
///     warning in a file the author cannot edit.
/// </remarks>
internal readonly struct PreparationHook(
    InvokerBindingsModel bindings, bool loadsEntities, LoadedValidationModel loadedValidation,
    EquatableArray<LoadFromQueryModel> loadsFromQueries)
{
    private const string Error = "global::Pragmatic.Result.IError";

    /// <summary>Whether the hook is overridden at all.</summary>
    public bool IsNeeded => loadsEntities || bindings.AnyRendered || loadedValidation.IsDeclared || ReadsQueries;

    /// <summary>Whether anything is read inside the operation's filter scopes.</summary>
    public bool Loads => loadsEntities || bindings.LoadsTheUser;

    private bool Awaits => loadsEntities || bindings.ReadsTheUser || loadedValidation.Async || ReadsQueries;

    private bool ReadsQueries => !loadsFromQueries.IsDefaultOrEmpty;

    /// <summary>The return type of the override, <c>async</c> when it awaits.</summary>
    public string ReturnType => Awaits
        ? $"async global::System.Threading.Tasks.Task<{Error}?>"
        : $"global::System.Threading.Tasks.Task<{Error}?>";

    /// <summary>The statements that write the bound properties of <paramref name="target" />.</summary>
    public IEnumerable<string> BindingStatements(string target, string services)
        => InvokerBindingEmitter.Statements(
            bindings.CurrentUser, bindings.Clock, target, services, "ct", Refuse);

    /// <summary>
    ///     The statements that load the signed-in user's entity into <paramref name="target" />: 401 for a
    ///     caller who is not signed in, 404 for an account with no user entity — what a query answers for
    ///     <c>[FromCurrentUser]</c>.
    /// </summary>
    /// <remarks>
    ///     Their own locals, not the bindings': the bindings resolve before the filter scopes and this after,
    ///     so the two readings are not the same one, and a module that declares both reads twice.
    /// </remarks>
    public IEnumerable<string> CurrentUserLoadStatements(string target, string services)
    {
        if (bindings.CurrentUserLoad is not { IsRendered: true } load)
            yield break;

        yield return $"var __signedIn = {services}.GetService(typeof(global::Pragmatic.Identity.ICurrentUser))";
        yield return "    as global::Pragmatic.Identity.ICurrentUser ?? global::Pragmatic.Identity.AnonymousUser.Instance;";
        yield return "if (!__signedIn.IsAuthenticated)";
        yield return "    " + Refuse("global::Pragmatic.Result.Http.UnauthorizedError.Create()");
        yield return $"var __signedInUser = await new {load.ResolverTypeFullName}(";
        yield return "    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
                     + ".GetRequiredService<global::Pragmatic.Persistence.Repository.IReadRepository<"
                     + $"{load.UserTypeFullName}>>({services}),";
        yield return "    __signedIn)";
        yield return "    .ResolveAsync(ct).ConfigureAwait(false);";
        yield return "if (__signedInUser is null)";
        yield return "    " + Refuse($"global::Pragmatic.Result.Http.NotFoundError.For(\"{load.UserTypeName}\", __signedIn.Id)");
        yield return $"{target}.SetCurrentUser(__signedInUser);";
        yield return "";
    }

    /// <summary>
    ///     The <c>[LoadFrom&lt;TQuery&gt;]</c> properties of <paramref name="target" />, each filled by its query
    ///     run through the query's own invoker — its validation, its permission, its read — and the operation
    ///     failed with the query's error when it fails.
    /// </summary>
    /// <remarks>
    ///     The query's invoker, not the boundary's internal facade: that one enters an internal call, and the
    ///     query's permission would not be asked. Before the operation's filter scopes, so the query reads with
    ///     its own filters, as it does wherever else it runs.
    /// </remarks>
    public IEnumerable<string> QueryStatements(string target, string services)
    {
        if (loadsFromQueries.IsDefaultOrEmpty)
            yield break;

        foreach (var load in loadsFromQueries)
        {
            // A property the query cannot fill is PRAG0458, an error: nothing is emitted for it.
            if (!load.IsResolved || load.ResultProblem is not null)
                continue;

            var query = load.Inputs.IsDefaultOrEmpty
                ? $"new {load.QueryTypeFullName}()"
                : $"new {load.QueryTypeFullName} {{ {string.Join(", ", load.Inputs.Select(i => $"{i.QueryProperty} = {target}.{i.OperationProperty}"))} }}";

            yield return $"var {load.Variable} = await new {load.QueryTypeFullName}.Invoker({services}).RunAsync({query}, ct).ConfigureAwait(false);";
            yield return $"if ({load.Variable}.IsFailure)";
            yield return "    " + Refuse($"{load.Variable}.Error");
            yield return $"{target}.{load.PropertyName} = {load.Variable}.Value;";
            yield return "";
        }
    }

    /// <summary>
    ///     The operation's <c>ValidateLoaded</c> rules, after everything is loaded: a failure ends the
    ///     invocation with the <c>ValidationError</c> itself — the 422 a rule declared on a property gives.
    /// </summary>
    public IEnumerable<string> LoadedValidationStatements(string target)
    {
        if (loadedValidation.Sync)
        {
            yield return $"var __loadedRules = {target}.ValidateLoaded();";
            yield return "if (__loadedRules.IsFailure)";
            yield return "    " + Refuse("__loadedRules");
        }

        if (loadedValidation.Async)
        {
            yield return $"var __loadedRulesAsync = await {target}.ValidateLoadedAsync(ct).ConfigureAwait(false);";
            yield return "if (__loadedRulesAsync.IsFailure)";
            yield return "    " + Refuse("__loadedRulesAsync");
        }
    }

    /// <summary>The last statement of the hook.</summary>
    public string Success => Awaits
        ? "return null;"
        : $"return global::System.Threading.Tasks.Task.FromResult<{Error}?>(null);";

    private string Refuse(string error) => Awaits
        ? $"return {error};"
        : $"return global::System.Threading.Tasks.Task.FromResult<{Error}?>({error});";
}
