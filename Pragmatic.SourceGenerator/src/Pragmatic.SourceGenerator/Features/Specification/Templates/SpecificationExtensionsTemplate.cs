using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Specification.Models;

namespace Pragmatic.SourceGenerator.Features.Specification.Templates;

/// <summary>
///     Emits, per entity and namespace, the two surfaces a declared specification is consumed through:
///     the queryable it filters, and the repository it is read from.
/// </summary>
/// <remarks>
///     <para>
///         Without it the call site names three things to say one —
///         <c>_items.Query().Where(KnowledgeSpecs.Confirmed().ToExpression())</c> — for a call the
///         framework can write. What it writes is the same call: the
///         extension delegates to the declared member, so the specification stays the one place the
///         predicate lives.
///     </para>
///     <para>
///         In the declaring type's namespace on purpose: the <c>using</c> that reaches the
///         specification reaches the extension, and nothing new has to be imported to use it.
///     </para>
/// </remarks>
internal sealed class SpecificationExtensionsTemplate : CSharpTemplate
{
    private const string ReadRepository = "global::Pragmatic.Persistence.Repository.IReadRepository";
    private const string Queryable = "global::System.Linq.IQueryable";
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string CancellationToken = "global::System.Threading.CancellationToken";

    private readonly string _entityFullTypeName;
    private readonly string _entityShortName;
    private readonly string _namespace;
    private readonly IReadOnlyList<DeclaredSpecificationModel> _specifications;

    public SpecificationExtensionsTemplate(
        string entityFullTypeName,
        string entityShortName,
        string ns,
        IReadOnlyList<DeclaredSpecificationModel> specifications)
    {
        _entityFullTypeName = entityFullTypeName;
        _entityShortName = entityShortName;
        _namespace = ns;
        _specifications = specifications;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Specification";
    protected override string? SourceInfo => $"{_entityShortName} specifications declared in {_namespace}";
    protected override string? TriggerInfo =>
        $"static members returning Specification<{_entityShortName}>";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Specifications",
            string.IsNullOrEmpty(_namespace) ? _entityShortName : $"{_namespace}.{_entityShortName}"),
        ToSourceText());

    protected override bool Validate() => _specifications.Count > 0;

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_namespace))
        {
            AppendNamespace(_namespace);
            AppendLine();
        }

        var accessibility = _specifications.All(s => s.IsPubliclyVisible) ? "public" : "internal";

        XmlSummary(
            $"The declared specifications of {_entityShortName}, on the queryable and on the repository.");
        XmlRemarks(
            "Generated from every static member returning a specification of " + _entityShortName
            + ". Each extension delegates to the member it is named after, so the predicate stays "
            + "where it was written.");

        AppendLine($"{accessibility} static partial class {_entityShortName}SpecificationExtensions");
        Block(() =>
        {
            for (var i = 0; i < _specifications.Count; i++)
            {
                if (i > 0)
                    AppendLine();

                RenderOne(_specifications[i]);
            }
        });
    }

    private void RenderOne(DeclaredSpecificationModel spec)
    {
        var call = spec.IsProperty
            ? $"{spec.ContainerFullTypeName}.{spec.MemberName}"
            : $"{spec.ContainerFullTypeName}.{spec.MemberName}({Arguments(spec)})";

        RenderQueryable(spec, call);
        AppendLine();
        RenderTerminal(spec, call, "Find", $"{Task}<global::System.Collections.Generic.List<{_entityFullTypeName}>>", "FindAsync");
        AppendLine();
        RenderTerminal(spec, call, "Count", $"{Task}<int>", "CountAsync");
        AppendLine();
        RenderTerminal(spec, call, "Any", $"{Task}<bool>", "ExistsAsync");
        AppendLine();
        RenderTerminal(spec, call, "First", $"{Task}<{_entityFullTypeName}?>", "FirstOrDefaultAsync",
            suffix: "OrDefaultAsync");
    }

    private void RenderQueryable(DeclaredSpecificationModel spec, string call)
    {
        XmlSummary($"Filters {_entityShortName} by the {spec.MemberName} specification.");
        XmlParam("query", "The queryable to filter.");
        foreach (var p in spec.Parameters)
            XmlParam(p.Name, $"Passed to {spec.MemberName}.");
        XmlReturns("The filtered queryable.");

        var parameters = Declaration(spec, $"this {Queryable}<{_entityFullTypeName}> query");

        AppendLine($"public static {Queryable}<{_entityFullTypeName}> {spec.MemberName}({parameters})");
        // Qualified: the file declares no using, and a consumer without System.Linq among its implicit
        // usings would find no Where on the queryable.
        Block(() => AppendLine($"return global::System.Linq.Queryable.Where(query, {call}.ToExpression());"));
    }

    /// <summary>
    ///     One of the four terminal reads the repository already offers, bound to the named
    ///     specification.
    /// </summary>
    /// <remarks>
    ///     The verb is not repeated where the specification already carries it: a specification called
    ///     <c>FindOrphans</c> gives <c>FindOrphansAsync</c>, not <c>FindFindOrphansAsync</c>. Same rule
    ///     and same reason as <c>NamingHelper.AppendSuffix</c>, on the other end of the name.
    /// </remarks>
    private void RenderTerminal(
        DeclaredSpecificationModel spec, string call, string verb, string returnType, string repositoryMethod,
        string suffix = "Async")
    {
        var name = spec.MemberName.StartsWith(verb, System.StringComparison.Ordinal)
            ? $"{spec.MemberName}{suffix}"
            : $"{verb}{spec.MemberName}{suffix}";

        XmlSummary($"Reads through the {spec.MemberName} specification.");
        XmlParam("repository", "The repository to read through.");
        foreach (var p in spec.Parameters)
            XmlParam(p.Name, $"Passed to {spec.MemberName}.");
        XmlParam("ct", "Cancellation token.");

        var parameters = Declaration(spec,
            $"this {ReadRepository}<{_entityFullTypeName}> repository",
            $"{CancellationToken} ct = default");

        AppendLine($"public static {returnType} {name}({parameters})");
        Block(() =>
        {
            AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(repository);");
            AppendLine($"return repository.{repositoryMethod}({call}, ct);");
        });
    }

    /// <summary>The receiver, then the specification's own parameters, then whatever trails.</summary>
    private static string Declaration(DeclaredSpecificationModel spec, string receiver, string? trailing = null)
    {
        var parts = new List<string> { receiver };
        parts.AddRange(spec.Parameters.Select(p => $"{p.TypeName} {p.Name}"));
        if (trailing is not null)
            parts.Add(trailing);
        return string.Join(", ", parts);
    }

    private static string Arguments(DeclaredSpecificationModel spec)
        => string.Join(", ", spec.Parameters.Select(p => p.Name));
}
