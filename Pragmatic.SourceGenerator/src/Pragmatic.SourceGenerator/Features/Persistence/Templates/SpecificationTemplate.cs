using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating specification classes for entities.
///     Generates ById and ByLogicKey specifications.
/// </summary>
internal sealed class SpecificationTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public SpecificationTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Entity] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Specs", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model.IsValid;
    }

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Specification");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        // Generate specifications container class
        RenderSpecificationsClass();
    }

    private void RenderSpecificationsClass()
    {
        var className = $"{_model.TypeName}Specifications";

        XmlSummary($"Specifications for querying {_model.TypeName} entities.");

        // Partial so the specifications an application names itself live in the same class as the
        // generated ones, under one name. Without it the two would have to be separate containers —
        // the generated Specifications nobody references, and a hand-written {Entity}Specs beside it —
        // and whoever wrote the second would have no reason to know the first existed.
        AppendLine($"public static partial class {className}");
        Block(() =>
        {
            RenderByIdSpecification();

            if (_model.LogicKeys.Length > 0)
            {
                AppendLine();
                RenderByLogicKeySpecification();
            }
        });
    }

    /// <summary>
    ///     The way an operation reaches a row by its domain key: an extension on
    ///     <c>IReadRepository&lt;TEntity&gt;</c>, the contract an action is allowed to depend on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Not only on the concrete <c>{Entity}.Repository</c>, which no operation can ask for: a
    ///         field of a concrete type is <c>PRAG0419</c> — the generator cannot tell an injected service
    ///         from plain state, so it is not injected — and <c>PRAG0527</c> on an endpoint. There, the
    ///         member the domain key exists to provide would have no declared way in, and the only route
    ///         that worked would be resolving the concrete repository from <c>IServiceProvider</c>.
    ///     </para>
    ///     <para>
    ///         An extension rather than an interface member because the signature is different for every
    ///         entity: <c>INavigationLoader</c> can be declared once, <c>GetByNumberAsync</c> cannot. It
    ///         delegates to the specification rendered just above, so the key's columns are written once
    ///         — and to <c>FirstOrDefaultAsync</c>, so the query filters apply exactly as they do on the
    ///         repository's own accessor.
    ///     </para>
    /// </remarks>
    private void RenderGetByLogicKeyAccessor(
        string entityType, System.Collections.Immutable.ImmutableArray<Models.LogicKeyPart> keys,
        string specName, string declaration, string arguments, string names)
    {
        var accessorName = $"GetBy{string.Join("And", keys.Select(k => k.Name))}Async";

        XmlSummary($"Gets the {_model.TypeName} with the given {names}, or null.");
        XmlParam("repository", "The repository to read through.");
        foreach (var key in keys)
            XmlParam(TemplateHelpers.ToCamelCase(key.Name), $"The {key.Name} part of the domain key.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The entity if found, null otherwise.");

        AppendLine($"public static global::System.Threading.Tasks.Task<{entityType}?> {accessorName}(");
        IncreaseIndent();
        AppendLine($"this global::Pragmatic.Persistence.Repository.IReadRepository<{entityType}> repository,");
        AppendLine($"{declaration},");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(repository);");
            AppendLine($"return repository.FirstOrDefaultAsync({specName}({arguments}), ct);");
        });
    }

    private void RenderByIdSpecification()
    {
        var entityType = $"global::{_model.FullTypeName}";
        var idType = GetFullIdType();

        XmlSummary($"Creates a composable specification that filters by PersistenceId.");
        XmlParam("id", "The entity ID to match.");
        XmlReturns("A composable specification that filters by the specified ID.");

        AppendLine($"public static global::Pragmatic.Specification.Specification<{entityType}> ById({idType} id)");
        Block(() =>
        {
            AppendLine($"return global::Pragmatic.Specification.Spec<{entityType}>.Where(e => e.PersistenceId.Equals(id));");
        });

        AppendLine();

        // Also generate IQueryable extension
        XmlSummary($"Filters {_model.TypeName} entities by PersistenceId.");
        XmlParam("query", "The queryable to filter.");
        XmlParam("id", "The entity ID to match.");
        XmlReturns("The filtered queryable.");

        AppendLine($"public static global::System.Linq.IQueryable<{entityType}> ById(this global::System.Linq.IQueryable<{entityType}> query, {idType} id)");
        Block(() =>
        {
            AppendLine("return query.Where(ById(id).ToExpression());");
        });
    }

    /// <summary>
    ///     The specification that addresses a row by its domain key.
    /// </summary>
    /// <remarks>
    ///     One part keeps the name and the single parameter it has always had — <c>ByCode(code)</c>.
    ///     More than one names every part: <c>ByCodeAndSeason(code, season)</c>, because a key of two
    ///     columns has no single value to call "the key".
    /// </remarks>
    private void RenderByLogicKeySpecification()
    {
        var entityType = $"global::{_model.FullTypeName}";
        var keys = _model.LogicKeys.AsImmutableArray();
        var methodName = $"By{string.Join("And", keys.Select(k => k.Name))}";
        var declaration = string.Join(", ",
            keys.Select(k => $"{FullTypeOf(k.TypeName)} {TemplateHelpers.ToCamelCase(k.Name)}"));
        var arguments = string.Join(", ", keys.Select(k => TemplateHelpers.ToCamelCase(k.Name)));
        var predicate = string.Join(" && ",
            keys.Select(k => $"e.{k.Name} == {TemplateHelpers.ToCamelCase(k.Name)}"));
        var names = string.Join(", ", keys.Select(k => k.Name));

        XmlSummary($"Creates a composable specification that filters by {names}.");
        foreach (var key in keys)
            XmlParam(TemplateHelpers.ToCamelCase(key.Name), $"The {key.Name} part of the domain key.");
        XmlReturns("A composable specification that filters by the specified domain key.");

        AppendLine($"public static global::Pragmatic.Specification.Specification<{entityType}> {methodName}({declaration})");
        Block(() =>
        {
            AppendLine($"return global::Pragmatic.Specification.Spec<{entityType}>.Where(e => {predicate});");
        });

        AppendLine();

        // Also generate IQueryable extension
        XmlSummary($"Filters {_model.TypeName} entities by {names}.");
        XmlParam("query", "The queryable to filter.");
        foreach (var key in keys)
            XmlParam(TemplateHelpers.ToCamelCase(key.Name), $"The {key.Name} part of the domain key.");
        XmlReturns("The filtered queryable.");

        AppendLine($"public static global::System.Linq.IQueryable<{entityType}> {methodName}(this global::System.Linq.IQueryable<{entityType}> query, {declaration})");
        Block(() =>
        {
            AppendLine($"return query.Where({methodName}({arguments}).ToExpression());");
        });

        AppendLine();
        RenderGetByLogicKeyAccessor(entityType, keys, methodName, declaration, arguments, names);
    }

    /// <summary>The same normalisation <see cref="GetFullLogicKeyType"/> does, for any part's type.</summary>
    private static string FullTypeOf(string typeName) => typeName switch
    {
        "Guid" => "global::System.Guid",
        "int" or "long" or "string" or "decimal" or "bool" => typeName,
        _ when typeName.Contains(".") => $"global::{typeName}",
        _ => typeName,
    };

    private string GetFullIdType()
    {
        return _model.IdType switch
        {
            "Guid" => "global::System.Guid",
            "int" => "int",
            "long" => "long",
            "string" => "string",
            _ when _model.IdType.Contains(".") => $"global::{_model.IdType}",
            _ => _model.IdType
        };
    }

    private string GetFullLogicKeyType()
    {
        var keyType = _model.LogicKeyType ?? "string";

        return keyType switch
        {
            "Guid" => "global::System.Guid",
            "int" => "int",
            "long" => "long",
            "string" => "string",
            _ when keyType.Contains(".") => $"global::{keyType}",
            _ => keyType
        };
    }
}
