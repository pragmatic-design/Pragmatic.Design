using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     The fields <c>[LoadEntity]</c> and <c>[LoadEntities]</c> promise, on the partial action or mutation,
///     and the setter its invoker hands the loaded entities through.
/// </summary>
/// <remarks>
///     One template for both: the declaration means the same thing on either, including a mutation.
/// </remarks>
internal sealed partial class LoadEntityTemplate : CSharpTemplate
{
    private readonly string _typeName;
    private readonly string _namespace;
    private readonly string _accessibility;
    private readonly bool _isValid;
    private readonly EquatableArray<LoadEntityModel> _loadEntities;

    public LoadEntityTemplate(ActionModel model)
        : this(model.TypeName, model.Namespace, model.Accessibility, model.IsValid, model.LoadEntities)
    {
    }

    public LoadEntityTemplate(MutationModel model)
        : this(model.TypeName, model.Namespace, model.Accessibility, model.IsValid, model.LoadEntities)
    {
    }

    private LoadEntityTemplate(
        string typeName, string @namespace, string accessibility, bool isValid, EquatableArray<LoadEntityModel> loadEntities)
    {
        _typeName = typeName;
        _namespace = @namespace;
        _accessibility = accessibility;
        _isValid = isValid;
        _loadEntities = loadEntities;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_typeName} from {_namespace}";
    protected override string? TriggerInfo => $"[LoadEntity] on {_typeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_typeName, "LoadEntity", _namespace),
        ToSourceText());

    protected override bool Validate() => _isValid && !_loadEntities.IsDefaultOrEmpty;

    /// <summary>
    ///     What an invoker's preparation hook runs: load each entity by the key the operation carries,
    ///     answer 404 for one that names nothing, then hand them over. The hook ends the method itself, after
    ///     the <c>ValidateLoaded</c> rules that read them.
    /// </summary>
    /// <param name="operation">The name of the invoker method's parameter holding the operation.</param>
    /// <param name="loadEntities">The declarations.</param>
    internal static IEnumerable<string> PreparationStatements(string operation, EquatableArray<LoadEntityModel> loadEntities)
    {
        var batchOf = Batches(loadEntities);
        foreach (var le in loadEntities)
        {
            // Loads of one entity by key share one query, rendered where the first of them is declared.
            if (batchOf.TryGetValue(le, out var batch))
            {
                if (ReferenceEquals(batch[0], le))
                    foreach (var statement in BatchStatements(operation, batch))
                        yield return statement;
                continue;
            }

            var variable = Variable(le);
            var stem = Stem(le);
            var notFound = $"return global::Pragmatic.Result.Http.NotFoundError.For(\"{le.EntityTypeShortName}\", ";
            var noRow = $"return global::Pragmatic.Result.Http.NotFoundError.For(\"{le.EntityTypeShortName}\");";

            if (le.ExistsOnly)
            {
                // [RequireExists]: an EXISTS through the repository — its filters, no row materialized — and a 404
                // naming the key when it answers no. A null key is not a reference, so nothing is asked.
                var reference = $"{operation}.{le.IdPropertyName}";
                if (le.IsOptional)
                {
                    // The key may be a path through the request ("Request.RoomTypeId"), which reads as
                    // written but cannot be part of an identifier.
                    var key = $"__{TemplateHelpers.ToCamelCase(le.IdPropertyName.Replace(".", ""))}Key";
                    yield return $"if ({reference} is {{ }} {key} && !await {Exists(le, key)}.ConfigureAwait(false))";
                    yield return $"    {notFound}{key}.ToString());";
                    continue;
                }

                yield return $"if (!await {Exists(le, reference)}.ConfigureAwait(false))";
                yield return $"    {notFound}{reference}.ToString());";
                continue;
            }

            if (le.IsBySpecification)
            {
                // The rule, not a key: the first row it matches, or every one. None is a 404 for the single
                // form — the operation needs the row — and an empty list for the list unless RequireAny.
                var rule = Rule(operation, le);
                if (le.IsMany)
                {
                    yield return $"global::System.Collections.Generic.IReadOnlyList<{le.EntityTypeFullName}> {variable} = await {le.RepositoryFieldName}.FindAsync({rule}, ct).ConfigureAwait(false);";
                    if (le.RequireAny)
                    {
                        yield return $"if ({variable}.Count == 0)";
                        yield return $"    {noRow}";
                    }
                    continue;
                }

                yield return $"var {variable} = await {ReadByRule(le, rule)}.ConfigureAwait(false);";
                yield return $"if ({variable} is null)";
                yield return $"    {noRow}";
                continue;
            }

            if (le.IsMany)
            {
                foreach (var statement in ListStatements(operation, le, variable, stem))
                    yield return statement;
                if (le.RequireAny)
                {
                    yield return $"if ({variable}.Count == 0)";
                    yield return $"    {noRow}";
                }
                continue;
            }

            if (le.IsOptional)
            {
                // The optional load: nothing is read without a key, and a key that names nothing is
                // still a 404 — "load it if given" is not "ignore it if wrong".
                var key = $"__{stem}Key";
                yield return $"{le.EntityTypeFullName}? {variable} = null;";
                yield return $"if ({operation}.{le.IdPropertyName} is {{ }} {key})";
                yield return "{";
                yield return $"    {variable} = await {Read(le, key)}.ConfigureAwait(false);";
                yield return $"    if ({variable} is null)";
                yield return $"        {notFound}{key}.ToString());";
                yield return "}";
                continue;
            }

            var given = $"{operation}.{le.IdPropertyName}";
            yield return $"var {variable} = await {Read(le, given)}.ConfigureAwait(false);";
            yield return $"if ({variable} is null)";
            yield return $"    {notFound}{(le.KeyTypeFullName == "string" ? given : $"{given}.ToString()")});";
        }

        var loaded = loadEntities.Where(le => !le.ExistsOnly).Select(Variable);
        yield return $"{operation}.SetLoadedEntities({string.Join(", ", loaded)});";
    }

    /// <summary>Whether a row of the entity has <paramref name="key" /> as its id, asked of the repository.</summary>
    private static string Exists(LoadEntityModel le, string key)
        => $"{le.RepositoryFieldName}.ExistsAsync(global::Pragmatic.Specification.Spec<{le.EntityTypeFullName}>.Where(__row => __row.PersistenceId.Equals({key})), ct)";

    /// <summary>
    ///     The rows a <c>[LoadEntities]</c> names: one query for the distinct keys, then one 404 naming every
    ///     key no row answered, then the rows in the order of the keys.
    /// </summary>
    /// <remarks>
    ///     <c>FindAsync</c> is the repository's filtered, tracked set — what <c>GetByIdAsync</c> reads — so a
    ///     row a filter hides is missing here as it is there. A null list is an empty one, and an empty one
    ///     is no query: <c>WHERE Id IN ()</c> answers nothing, and asking costs a round trip.
    /// </remarks>
    private static IEnumerable<string> ListStatements(
        string operation, LoadEntityModel le, string variable, string stem)
    {
        const string linq = "global::System.Linq.Enumerable";
        var given = $"__{stem}Given";
        var keys = $"__{stem}Keys";
        var rows = $"__{stem}Rows";
        var byKey = $"__{stem}ByKey";
        var missing = $"__{stem}Missing";

        yield return $"global::System.Collections.Generic.IEnumerable<{le.KeyTypeFullName}>? {given} = {operation}.{le.IdPropertyName};";
        yield return $"{le.KeyTypeFullName}[] {keys} = {given} is null ? [] : {linq}.ToArray({linq}.Distinct({given}));";
        yield return $"global::System.Collections.Generic.IReadOnlyList<{le.EntityTypeFullName}> {variable} = [];";
        yield return $"if ({keys}.Length > 0)";
        yield return "{";
        yield return $"    var {rows} = await {le.RepositoryFieldName}.FindAsync(";
        yield return $"        global::Pragmatic.Specification.Spec<{le.EntityTypeFullName}>.Where(__row => {linq}.Contains({keys}, __row.PersistenceId)),";
        yield return "        ct).ConfigureAwait(false);";
        yield return $"    var {byKey} = {linq}.ToDictionary({rows}, __row => __row.PersistenceId);";
        yield return $"    var {missing} = {linq}.ToArray({linq}.Where({keys}, __key => !{byKey}.ContainsKey(__key)));";
        yield return $"    if ({missing}.Length > 0)";
        yield return $"        return global::Pragmatic.Result.Http.NotFoundError.ForAll(\"{le.EntityTypeShortName}\", {missing});";
        yield return $"    {variable} = {linq}.ToArray({linq}.Select({keys}, __key => {byKey}[__key]));";
        yield return "}";
    }

    /// <summary>
    ///     The read permissions the loads ask — <c>RequireReadPermission = true</c> — checked together before
    ///     anything is read: a caller who may not read the rows is refused 403 without them being read.
    /// </summary>
    /// <param name="services">The invoker's service provider expression.</param>
    /// <param name="operationType">The operation's type, as the refusal names it.</param>
    /// <param name="loadEntities">The declarations.</param>
    internal static IEnumerable<string> PermissionStatements(
        string services, string operationType, EquatableArray<LoadEntityModel> loadEntities)
    {
        var permissions = loadEntities
            .Where(le => le.RequireReadPermission && le.ReadPermission is not null)
            .Select(le => le.ReadPermission!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (permissions.Count == 0)
            yield break;

        var literals = string.Join(", ", permissions.Select(p => "\"" + StringHelper.CSharpLiteral(p) + "\""));
        yield return "var __preloadRefused = await global::Pragmatic.Actions.Pipeline.PreloadAuthorization.RequireAllAsync(";
        yield return $"    {services}, [{literals}], typeof({operationType}), ct).ConfigureAwait(false);";
        yield return "if (__preloadRefused is not null)";
        yield return "    return __preloadRefused;";
        yield return "";
    }

    /// <summary>The call of the rule a load reads by, its parameters named and bound to the operation's properties.</summary>
    private static string Rule(string operation, LoadEntityModel le)
        => le.SpecificationIsInvocation
            ? $"{le.SpecificationMember}({string.Join(", ", le.SpecificationArguments.Select(a => $"{a.Parameter}: {operation}.{a.Property}"))})"
            : le.SpecificationMember!;

    /// <summary>The first row <paramref name="rule" /> matches, with the <c>Include</c> paths if any.</summary>
    /// <remarks>
    ///     Without an include it is the repository's own <c>FirstOrDefaultAsync(spec)</c>. With one it is the
    ///     repository's filtered, tracked <c>Query()</c> — what that method reads — with an EF Core
    ///     <c>Include</c> per path and the rule's expression.
    /// </remarks>
    private static string ReadByRule(LoadEntityModel le, string rule)
    {
        if (le.Includes.Count == 0)
            return $"{le.RepositoryFieldName}.FirstOrDefaultAsync({rule}, ct)";

        const string ef = "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
        var query = $"{le.RepositoryFieldName}.Query()";
        foreach (var path in le.Includes)
            query = $"{ef}.Include({query}, \"{path}\")";

        return $"{ef}.FirstOrDefaultAsync(global::System.Linq.Queryable.Where({query}, ({rule}).ToExpression()), ct)";
    }

    /// <summary>The local a loaded entity — or list of them — is held in until it is handed over.</summary>
    /// <remarks>
    ///     <para>
    ///         Named after the field, which is unique on the operation — not after the entity: two loads of one
    ///         entity would both come out <c>employee</c>, a local declared twice and a setter with two
    ///         parameters of one name.
    ///     </para>
    ///     <para>
    ///         Escaped, because an entity named <c>Case</c>, <c>Event</c> or <c>Lock</c> camel-cases to a
    ///         keyword and <c>var case = …</c> is not C#. <c>CSharpTemplate</c>'s own parameter rendering
    ///         escapes the setter's parameter; the local and the assignment are composed here as strings,
    ///         so they are escaped here.
    ///     </para>
    /// </remarks>
    private static string Variable(LoadEntityModel le) => IdentifierHelper.EscapeIfKeyword(Stem(le));

    /// <summary>
    ///     The same name <b>unescaped</b>, for the locals composed out of it (<c>__{stem}Key</c>, …).
    /// </summary>
    /// <remarks>
    ///     ⚠️ The <c>@</c> belongs on an identifier and not inside one: <c>__@caseKey</c> is as broken as
    ///     <c>case</c>. A derived name is a new identifier that happens to contain this one, and it is not a
    ///     keyword whatever the stem is.
    /// </remarks>
    private static string Stem(LoadEntityModel le)
        => TemplateHelpers.ToCamelCase(le.FieldName.TrimStart('_') is { Length: > 0 } stem
            ? stem
            : le.IsMany ? StringHelper.Pluralize(le.EntityTypeShortName) : le.EntityTypeShortName);

    /// <summary>The type of the field, and of the setter's parameter, for <paramref name="le" />.</summary>
    private static string FieldType(LoadEntityModel le)
        => le.IsMany
            ? $"global::System.Collections.Generic.IReadOnlyList<{le.EntityTypeFullName}>"
            : le.IsOptional ? $"{le.EntityTypeFullName}?" : le.EntityTypeFullName;

    /// <summary>The read of one entity by <paramref name="key" />, with its <c>Include</c> paths if any.</summary>
    /// <remarks>
    ///     Without an include it is the repository's own <c>GetByIdAsync</c>, unchanged — or, for a key that is the
    ///     logic key (<c>By</c>), the <c>GetBy{Key}Async</c> the generator writes beside <c>By{Key}</c>, which reads
    ///     through the same <c>FirstOrDefaultAsync</c>. With one it is the repository's filtered, tracked
    ///     <c>Query()</c> — what those read — with an EF Core <c>Include</c> per path: there is no lazy loading, and
    ///     a navigation not included is empty.
    /// </remarks>
    private static string Read(LoadEntityModel le, string key)
    {
        if (le.Includes.Count == 0)
            return le.LogicKeyMember is { } member
                ? $"{le.LogicKeyLookupClass}.GetBy{member}Async({le.RepositoryFieldName}, {key}, ct)"
                : $"{le.RepositoryFieldName}.GetByIdAsync({key}, ct)";

        const string ef = "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
        var query = $"{le.RepositoryFieldName}.Query()";
        foreach (var path in le.Includes)
            query = $"{ef}.Include({query}, \"{path}\")";

        return le.LogicKeyMember is { } byMember
            ? $"{ef}.FirstOrDefaultAsync(global::System.Linq.Queryable.Where({query}, {le.LogicKeyLookupClass}.By{byMember}({key}).ToExpression()), ct)"
            : $"{ef}.FirstOrDefaultAsync({query}, e => e.PersistenceId.Equals({key}), ct)";
    }

    public override void RenderFile()
    {
        AppendNamespace(_namespace);
        AppendLine();

        Class(_typeName, RenderClassBody,
            accessModifier: TemplateHelpers.ParseAccessibility(_accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderClassBody()
    {
        var repositories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var le in _loadEntities)
        {
            if (le.ExistsOnly)
            {
                // Nothing is read, so there is no row to hold: only the repository the EXISTS goes through.
            }
            else if (le.IsMany)
            {
                XmlSummary(le.IsBySpecification
                    ? $"The pre-loaded <c>{le.EntityTypeShortName}</c> rows the rule matches."
                    : $"The pre-loaded <c>{le.EntityTypeShortName}</c> rows named by <c>{le.IdPropertyName}</c>, in the order of the keys. A key that names no row was answered 404 by the invoker.");
                Field(le.FieldName, FieldType(le), AccessModifier.Private, initializer: "[]");
            }
            else if (le.IsOptional)
            {
                XmlSummary($"The pre-loaded <c>{le.EntityTypeShortName}</c> entity, or null when <c>{le.IdPropertyName}</c> is null. A key that names no row was answered 404 by the invoker.");
                Field(le.FieldName, $"{le.EntityTypeFullName}?", AccessModifier.Private);
            }
            else
            {
                XmlSummary($"The pre-loaded <c>{le.EntityTypeShortName}</c> entity. Guaranteed non-null once the invoker has prepared the operation.");
                Field(le.FieldName, $"{le.EntityTypeFullName}", AccessModifier.Private, initializer: "null!");
            }

            // The read repository used to load the entity, injected via SetDependencies. Declared here
            // (not by the user) so [LoadEntity] needs no hand-written repository field — once per name: a
            // [RequireExists] and a [LoadEntity] of the same entity go through the same one.
            if (!repositories.Add(le.RepositoryFieldName))
                continue;

            XmlSummary($"Read repository injected to pre-load the <c>{le.EntityTypeShortName}</c>.");
            Field(le.RepositoryFieldName, le.RepositoryTypeFullName, AccessModifier.Private, initializer: "null!");
        }

        AppendLine();

        XmlSummary("Sets the pre-loaded entities.");

        var parameters = _loadEntities
            .Where(le => !le.ExistsOnly)
            .Select(le => new MethodParameter(FieldType(le), Variable(le)))
            .ToList();

        Method("SetLoadedEntities", RenderSetBody, "void", parameters, AccessModifier.Internal);
    }

    private void RenderSetBody()
    {
        foreach (var le in _loadEntities.Where(le => !le.ExistsOnly))
            AppendLine($"this.{le.FieldName} = {Variable(le)};");
    }
}
