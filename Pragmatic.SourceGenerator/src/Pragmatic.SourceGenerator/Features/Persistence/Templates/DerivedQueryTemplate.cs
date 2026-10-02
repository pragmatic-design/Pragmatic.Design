using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Writes the query type derived from a specification: the class a author would have written beside
///     the rule, and did.
/// </summary>
/// <remarks>
///     <para>
///         It declares the specification's parameters as inputs, the paging surface when it was asked
///         for, and one property holding the specification itself. Everything else — <c>Apply()</c>,
///         <c>ToSpecification()</c>, the projection, the endpoint — comes from the same templates that
///         serve a hand-written query, fed the same model. That reuse is the test of whether the
///         derivation is right: a derived type that needed its own renderer would not be a query.
///     </para>
///     <para>
///         ⚠️ The specification property is an <b>expression-bodied</b> member, evaluated per instance:
///         the factory takes the inputs, and those are only known once the query is bound. A field
///         initialised at construction would capture whatever the inputs were before binding, which is
///         nothing.
///     </para>
/// </remarks>
internal sealed class DerivedQueryTemplate : CSharpTemplate
{
    private const string PagedInput = "global::Pragmatic.Persistence.Query.IPagedInput";
    private const string Specification = "global::Pragmatic.Specification.Specification";

    private readonly DerivedQueryModel _model;

    public DerivedQueryTemplate(DerivedQueryModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.MemberName} from {_model.ContainerFullTypeName}";
    protected override string? TriggerInfo => $"[Query] on the specification {_model.MemberName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "DerivedQuery", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"The {_model.MemberName} rule, as a query.");
        XmlRemarks(
            "Derived from the specification of the same name: the rule stays where it was written and "
            + "this type adds what a query has and a predicate cannot — the inputs, the page, the "
            + "projection and the route.");

        var interfaces = _model.Paged ? $" : {PagedInput}" : "";

        AppendLine($"public partial class {_model.TypeName}{interfaces}");
        Block(() =>
        {
            foreach (var input in _model.Inputs)
            {
                XmlSummary($"Passed to {_model.MemberName}.");
                AppendLine($"public {input.TypeName} {input.PropertyName} {{ get; init; }}{Initialiser(input)}");
                AppendLine();
            }

            if (_model.Paged)
            {
                XmlSummary("The page to read.");
                AppendLine("public int Page { get; init; } = 1;");
                AppendLine();

                XmlSummary("How many per page.");
                AppendLine("public int PageSize { get; init; } = 20;");
                AppendLine();
            }

            XmlSummary("The rule this query reads by.");
            XmlRemarks(
                "Recognised by type, like any specification a query declares, so Apply() and "
                + "ToSpecification() are rendered by the ordinary templates.");
            AppendLine(
                $"public {Specification}<global::{_model.EntityTypeFullName}> {DerivedQueryModel.SpecificationPropertyName}");
            AppendLine($"    => {Call()};");
        });
    }

    /// <summary>A non-nullable reference input needs an initialiser or the compiler warns on it.</summary>
    private static string Initialiser(DerivedQueryInput input)
        => input.IsNullable || IsValueLike(input.TypeName) ? "" : " = default!;";

    private static bool IsValueLike(string typeName)
        => typeName is "int" or "long" or "bool" or "decimal" or "double" or "float"
            or "global::System.Guid" or "global::System.DateTime" or "global::System.DateTimeOffset";

    private string Call()
        => _model.IsProperty
            ? $"{_model.ContainerFullTypeName}.{_model.MemberName}"
            : $"{_model.ContainerFullTypeName}.{_model.MemberName}({string.Join(", ", _model.Inputs.Select(i => i.PropertyName))})";
}
