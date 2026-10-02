using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

internal sealed class MutationAutoMapTemplate : Mapping.Templates.ChildWritingTemplate
{
    private readonly MutationModel _model;

    public MutationAutoMapTemplate(MutationModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} AutoMap to {_model.EntityTypeName}";
    protected override string? TriggerInfo => $"[Mutation] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "ApplyToEntity", _model.Namespace),
        ToSourceText());

    protected override bool Validate() =>
        _model is { IsValid: true, IsDelete: false } && _model.WritesSomething;

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        var entityCref = _model.EntityFullTypeName.Replace("global::", string.Empty);
        XmlSummary($"Auto-generated property mapping from <see cref=\"{_model.TypeName}\"/> to <see cref=\"{entityCref}\"/>.");

        Class(_model.TypeName, RenderBody,
            accessModifier: TemplateHelpers.ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        var entityType = _model.EntityFullTypeName;

        XmlSummary("Auto-generated property mapping to entity via generated setters.");
        XmlParam("entity", "The entity to apply property values to.");

        // Where Mapping is present the body is Mapping's: a mutation is a mapping classified by a
        // different attribute, and ApplyToLoaded carries converters, renames and declared targets this
        // template does not. What stays here is the override the invoker calls.
        if (_model.MappingOwnsTheBody)
        {
            ExpressionMethod("ApplyToEntity", "ApplyToLoaded(entity)", "void",
                new List<MethodParameter> { new(entityType, "entity") },
                AccessModifier.Public,
                new MethodModifiers { IsOverride = true });
        }
        else
        {
            Method("ApplyToEntity", () => RenderMappings(entityType), "void",
                new List<MethodParameter> { new(entityType, "entity") },
                AccessModifier.Public,
                new MethodModifiers { IsOverride = true });
        }

        RenderWrittenNavigations();
        RenderNestedOperations();
        RenderValidateNestedTree();
    }

    /// <summary>
    ///     The rules of this write and of everything it carries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The validator the Validation feature generates already walks collections of validatables,
    ///         but it is emitted <b>only for a type with rules of its own</b>. A child mutation with none
    ///         does not get one, and with it goes the loop over <i>its</i> children: the chain would break
    ///         at the first empty link, and nothing below it would be checked. The Conformance case
    ///         <c>TheRuleThreeLevelsDown</c> covers it.
    ///     </para>
    ///     <para>
    ///         Emitted on <b>every</b> mutation, even when it has nothing to say: the parent calls it
    ///         without knowing what is below, and a missing method and an empty one cannot be told apart
    ///         at the call site — only one of them compiles. The recursion happens at run time, as for
    ///         <c>WrittenNavigations</c>, because this transform sees one mutation only.
    ///     </para>
    ///     <para>
    ///         ⚠️ The call to the mutation's own rules is emitted only when the type will really have a
    ///         <c>Validate()</c>: predicted, not looked up. Emitting it regardless is a <c>CS1061</c>
    ///         inside a generated file.
    ///     </para>
    /// </remarks>
    private void RenderValidateNestedTree()
    {
        // ⚠️ The method names ValidationError: without Pragmatic.Validation referenced that is CS0234 in a
        // generated file. Where there is no validation there is nothing to validate either.
        if (!_model.ValidationIsAvailable)
            return;

        var children = _model.Children.Where(c => c.IsChildMutation).ToList();

        AppendLine();
        XmlSummary("Validates this write and everything nested inside it.");
        AppendLine("public global::Pragmatic.Validation.Types.ValidationError? ValidateNestedTree()");
        Block(() =>
        {
            AppendLine("global::Pragmatic.Validation.Types.ValidationError? combined = null;");

            // One name per accumulation: two `is { } __e` in the same scope are CS0136.
            var slot = 0;

            void Accumulate(string expression)
            {
                var name = $"__e{slot++}";
                AppendLine($"if ({expression} is {{ IsFailure: true }} {name})");
                IncreaseIndent();
                AppendLine($"combined = combined is null ? {name} : combined.Value.Combine({name});");
                DecreaseIndent();
            }

            RenderConversionChecks(Accumulate);

            if (_model.HasOwnValidator)
                Accumulate("Validate()");

            foreach (var child in children)
            {
                AppendLine($"if ({child.PropertyName} is not null)");
                Block(() =>
                {
                    if (child.IsCollection)
                    {
                        AppendLine($"foreach (var __element in {child.PropertyName})");
                        Block(() => Accumulate("__element.ValidateNestedTree()"));
                        return;
                    }

                    Accumulate($"{child.PropertyName}.ValidateNestedTree()");
                });
            }

            AppendLine("return combined;");
        });
    }

    /// <summary>
    ///     The values the write will have to convert, checked before it tries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The write path turns a string into what the entity declares by calling <c>Parse</c>, and a
    ///         <c>Parse</c> throws on a value it cannot read. Nothing catches that exception: it would
    ///         leave the invoker and reach ASP.NET, and the caller would get a <b>500</b> for a body they
    ///         sent.
    ///     </para>
    ///     <para>
    ///         The check lives here and not in a filter that catches <c>FormatException</c>, because a
    ///         <c>FormatException</c> raised by the domain would be blamed on the caller by mistake. The
    ///         generator already knows the conversion exists; it can also tell, beforehand, whether the
    ///         value passes it.
    ///     </para>
    ///     <para>
    ///         ⚠️ It lives inside <c>ValidateNestedTree</c> on purpose. A method of its own would need a
    ///         second call in the invoker and a second recursion over the children; this way a nested
    ///         child with a fallible conversion is covered by the same traversal that already carries its
    ///         rules, without anything naming it.
    ///     </para>
    /// </remarks>
    private void RenderConversionChecks(Action<string> accumulate)
    {
        foreach (var property in _model.MappedProperties.Where(p => p.CanConvertCheck is not null))
        {
            // An absent value is not an unreadable one: if the property is required, [Required] says so,
            // and saying it twice would give two messages for one defect.
            var guarded = property.IsNullable
                ? $"this.{property.MutationPropertyName} is null || {property.CanConvertCheck}"
                : property.CanConvertCheck;

            AppendLine($"if (!({guarded}))");
            Block(() => accumulate(
                "global::Pragmatic.Validation.Types.ValidationError.Valid.WithFor("
                + $"nameof({property.MutationPropertyName}), \"validation.conversion\", "
                + $"(\"expected\", \"{property.ExpectedShape}\"))"));
        }
    }

    /// <summary>
    ///     The operations this write traverses: the children, and the children's children.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A nested child does not go through its own invoker — a direct call writes it — so nothing
    ///         else would check its <c>[RequirePermission]</c>, and the parent would be a door around the
    ///         child's rules. The invoker walks this list and asks, for each type, the same check it makes
    ///         for itself, so the permission holds wherever that operation is reached.
    ///     </para>
    ///     <para>
    ///         Composed at <b>run time</b> from the children's lists, like <c>WrittenNavigations</c>: this
    ///         transform sees one mutation only, and the grandchildren belong to another's model. Emitted
    ///         even when empty, because the parent names it without knowing whether the child has any.
    ///     </para>
    /// </remarks>
    private void RenderNestedOperations()
    {
        var children = _model.Children.Where(c => c.IsChildMutation).ToList();

        AppendLine();
        XmlSummary("The operations this write passes through: the children, and theirs.");

        if (children.Count == 0)
        {
            AppendLine("public static global::System.Collections.Generic.IReadOnlyList<global::System.Type> "
                + "NestedOperations { get; } = [];");
            return;
        }

        AppendLine("public static global::System.Collections.Generic.IReadOnlyList<global::System.Type> "
            + "NestedOperations { get; } =");
        AppendLine("[");
        IncreaseIndent();
        foreach (var child in children)
        {
            AppendLine($"typeof({child.ChildDtoFullTypeName}),");
            AppendLine($".. {child.ChildDtoFullTypeName}.NestedOperations,");
        }

        DecreaseIndent();
        AppendLine("];");
    }

    /// <summary>
    ///     The navigations this mutation writes, deep and prefixed — the same contract a
    ///     <c>[MapTo]</c> DTO publishes, because a mutation is now the child shape too.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The invoker includes these before applying: a merge that cannot see the existing
    ///         children removes none and adds all. Emitted even when empty, so a parent can name it
    ///         unconditionally — a missing list and an empty one are indistinguishable at the call
    ///         site, and only one of them compiles.
    ///     </para>
    ///     <para>
    ///         The deeper levels are composed at <b>runtime</b> from each child's own list rather than
    ///         walked here. This transform sees one mutation; the grandchildren belong to another one's
    ///         model. Naming the child's static is safe because the same generator writes both in the
    ///         same compilation — the hazard is naming what a <em>different</em> generator will write.
    ///     </para>
    /// </remarks>
    private void RenderWrittenNavigations()
    {
        var written = _model.Children.Where(c => c.IsWritable).ToList();

        AppendLine();
        XmlSummary("The navigation paths this mutation writes, deep and prefixed.");

        if (written.Count == 0)
        {
            AppendLine("public static global::System.Collections.Generic.IReadOnlyList<string> "
                + "WrittenNavigations { get; } = [];");
            return;
        }

        AppendLine("public static global::System.Collections.Generic.IReadOnlyList<string> "
            + "WrittenNavigations { get; } =");
        AppendLine("[");
        IncreaseIndent();

        foreach (var child in written)
        {
            AppendLine($"\"{child.TargetPropertyName}\",");

            if (child.IsChildMutation)
                AppendLine($".. global::System.Linq.Enumerable.Select({child.ChildDtoFullTypeName}"
                    + $".WrittenNavigations, __p => \"{child.TargetPropertyName}.\" + __p),");
        }

        DecreaseIndent();
        AppendLine("];");
    }

    private void RenderMappings(string entityType)
    {
        RenderScalarAssignments();
        RenderChildren();
    }

    /// <summary>
    ///     The children the mutation carries, merged into the aggregate after its own properties.
    /// </summary>
    /// <remarks>
    ///     After, because a child's merge may depend on a scalar the same mutation is setting, and
    ///     because this is the order <c>ApplyToEntity</c> already promises: the generated mapping runs,
    ///     then the author's <c>ApplyAsync</c>. A child written here is one the author does not have to
    ///     add by hand.
    /// </remarks>
    private void RenderChildren()
    {
        foreach (var child in _model.Children)
        {
            if (!child.IsWritable)
                continue;

            AppendLine();
            RenderChildWrite(
                new ChildWriteModel
                {
                    PropertyName = child.PropertyName,
                    TargetPropertyName = child.TargetPropertyName,
                    IsCollection = child.IsCollection,
                    Collection = child.Collection,
                    IsChildMutation = child.IsChildMutation,
                    ChildEntityFullTypeName = child.ChildEntityFullTypeName,
                    ChildEntityHasParameterlessFactory = child.ChildEntityHasParameterlessFactory,
                    CanCreate = child.CanCreate,
                    CanPatch = child.CanPatch,
                    EntityHasPrivateSetter = child.EntityHasPrivateSetter,
                    ReferenceStrategy = child.ReferenceStrategy,
                },
                "entity");
        }
    }

    private void RenderScalarAssignments()
    {
        foreach (var prop in _model.MappedProperties)
        {
            // Set{Name} exists only where the setter is not public — it is the change-tracking wrapper
            // for what the outside cannot reach. An entity written the ordinary way, { get; set; }, is
            // assigned directly; calling the wrapper anyway named a method nobody generates.
            string Write(string value) => prop.EntityHasPublicSetter
                ? $"entity.{prop.EntityPropertyName} = {value};"
                : $"entity.Set{prop.EntityPropertyName}({value});";

            if (prop.IsNullable)
            {
                var localVar = char.ToLowerInvariant(prop.MutationPropertyName[0]) + prop.MutationPropertyName.Substring(1);
                If($"this.{prop.MutationPropertyName} is {{ }} {localVar}", () =>
                {
                    AppendLine(Write(localVar));
                });
            }
            else
            {
                AppendLine(Write($"this.{prop.MutationPropertyName}"));
            }
        }
    }
}
