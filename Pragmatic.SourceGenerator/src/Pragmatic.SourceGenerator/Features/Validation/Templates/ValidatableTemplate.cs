using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>
///     Template for generating the Validate() method for a validatable type.
/// </summary>
internal sealed partial class ValidatableTemplate : CSharpTemplate
{
    private readonly ValidatableModel _model;

    public ValidatableTemplate(ValidatableModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Validation";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Validatable] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(NestedTypeName(), "Validator", _model.Namespace),
        ToSourceText());

    /// <summary>
    ///     The type's name qualified by its containers, so two nested types with the same simple name
    ///     do not write to the same file.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A duplicate hint name is answered by Roslyn with <c>CS8785</c> — a warning — and the whole
    ///     generator's output is discarded. A build without <c>--warnaserror</c> succeeds with every
    ///     generated file missing.
    /// </remarks>
    private string NestedTypeName() =>
        _model.ContainingTypes.Length == 0
            ? _model.TypeName
            : string.Join(".", _model.ContainingTypes.Select(c => c.TypeName)) + "." + _model.TypeName;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Validation");
        AddUsing("Pragmatic.Validation.Attributes");
        AddUsing("Pragmatic.Validation.Types");

        if (_model.IsEntity)
            AddUsing("System.Collections.Generic");

        if (!string.IsNullOrEmpty(_model.Namespace))
            AppendNamespace(_model.Namespace);

        AppendLine();

        var modifiers = new ClassModifiers { Partial = true };
        var interfaces = new List<string> { "ISyncValidator" };

        // Custom cross-property attributes (a user ValidationAttribute with RequiresInstance) read
        // sibling values through IPropertyValueProvider. The built-in comparison attributes are
        // inlined and do not need it, so only emit the interface when a custom one is present.
        if (NeedsPropertyValueProvider())
            interfaces.Add("IPropertyValueProvider");

        // Reopen every enclosing type around the validator so the nesting survives. Each one must be
        // partial, which the feature checks before it gets here — emitting a second declaration of a
        // type that is not partial is an error inside a file the author cannot open.
        RenderInsideContainer(0, () => RenderValidatedType(interfaces, modifiers));
    }

    private void RenderInsideContainer(int depth, Action body)
    {
        if (depth >= _model.ContainingTypes.Length)
        {
            body();
            return;
        }

        var container = _model.ContainingTypes[depth];
        var modifiers = new ClassModifiers { Partial = true, IsStatic = container.IsStatic };
        var access = ToAccessModifier(container.Accessibility);

        void Inner() => RenderInsideContainer(depth + 1, body);

        switch (container.TypeKind)
        {
            case "record":
                Record(container.TypeName, Inner, accessModifier: access, modifiers: modifiers);
                break;
            case "record struct":
                RecordStruct(container.TypeName, Inner, accessModifier: access, modifiers: modifiers);
                break;
            case "struct":
                Struct(container.TypeName, Inner, null, access, modifiers);
                break;
            default:
                Class(container.TypeName, Inner, accessModifier: access, modifiers: modifiers);
                break;
        }
    }

    private void RenderValidatedType(List<string> interfaces, ClassModifiers modifiers)
    {
        switch (_model.TypeKind)
        {
            case "record":
                Record(_model.TypeName, RenderBody, interfaces: interfaces, accessModifier: GetAccessModifier(),
                    modifiers: modifiers);
                break;
            case "record struct":
                RecordStruct(_model.TypeName, RenderBody, interfaces: interfaces,
                    accessModifier: GetAccessModifier(), modifiers: modifiers);
                break;
            case "struct":
                Struct(_model.TypeName, RenderBody, interfaces, GetAccessModifier(), modifiers);
                break;
            default:
                Class(_model.TypeName, RenderBody, interfaces: interfaces, accessModifier: GetAccessModifier(),
                    modifiers: modifiers);
                break;
        }
    }

    private void RenderBody()
    {
        if (_model.IsEntity)
            RenderEntityValidation();
        else
            RenderValidateMethod();

        if (NeedsPropertyValueProvider())
        {
            AppendLine();
            RenderPropertyValueProvider();
        }
    }

    // True when the type carries a custom (Unknown-kind) attribute that requires the parent instance,
    // i.e. a user-defined cross-property ValidationAttribute reached via IsValid(value, instance).
    private bool NeedsPropertyValueProvider()
        => _model.Properties.Any(p => p.Attributes.Any(a =>
            a.RequiresInstance && a.Kind == ValidationKind.Unknown));

    // Reflection-free property access for custom cross-property attributes (the built-in comparison
    // attributes are inlined and never route through here). Every readable property, not only the
    // validated ones: the sibling a custom rule reads usually carries no rule of its own, and a name
    // missing here reads as null — "the rule does not apply" — so the rule passed, always.
    private void RenderPropertyValueProvider()
    {
        XmlSummary("Resolves a property value by name for cross-property validation (no reflection).");
        XmlParam("propertyName", "The name of the property to read.");
        XmlReturns("The property value, or null when the name is not a readable property.");
        Method("GetPropertyValue", () =>
        {
            AppendLine("return propertyName switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var name in _model.AllPropertyNames)
                AppendLine($"nameof({name}) => {name},");
            AppendLine("_ => null");
            DecreaseIndent();
            AppendLine("};");
        }, "object?", new List<MethodParameter> { new("string", "propertyName") });
    }

    #region Non-Entity Validation

    private void RenderValidateMethod()
    {
        if (_model.HasGroups)
        {
            XmlSummary("Validates this instance and returns a ValidationError.");
            XmlReturns("A ValidationError with IsSuccess=true if valid, or IsFailure=true with issues if invalid.");
            Method("Validate", () => AppendLine("return Validate(group: null);"), "ValidationError");
            AppendLine();

            XmlSummary("Validates this instance, optionally filtering by validation group.");
            XmlParam("group", "The validation group to run. When null, all validations run.");
            XmlReturns("A ValidationError with IsSuccess=true if valid, or IsFailure=true with issues if invalid.");
            Method("Validate", RenderValidateBody, "ValidationError",
                new List<MethodParameter> { new("string?", "group") { DefaultValue = "null" } });
        }
        else
        {
            XmlSummary("Validates this instance and returns a ValidationError.");
            XmlReturns("A ValidationError with IsSuccess=true if valid, or IsFailure=true with issues if invalid.");
            Method("Validate", RenderValidateBody, "ValidationError");
        }
    }

    private void RenderValidateBody()
    {
        AppendLine("var error = ValidationError.Valid;");
        AppendLine();
        foreach (var prop in _model.Properties)
        {
            RenderPropertyValidation(prop);
            AppendLine();
        }
        AppendLine("return error;");
    }

    #endregion

    #region Entity Validation (change-aware)

    private void RenderEntityValidation()
    {
        XmlSummary("Validates all properties of this entity.");
        XmlReturns("A ValidationError with IsSuccess=true if valid, or IsFailure=true with issues if invalid.");
        Method("Validate", () => AppendLine("return Validate(modifiedProperties: null);"), "ValidationError");
        AppendLine();

        XmlSummary("Validates only the specified modified properties. When modifiedProperties is null, validates all.");
        XmlParam("modifiedProperties", "The set of modified property names, or null to validate all.");
        XmlReturns("A ValidationError with IsSuccess=true if valid, or IsFailure=true with issues if invalid.");
        Method("Validate", RenderChangeAwareValidateBody, "ValidationError",
            new List<MethodParameter> { new("IReadOnlySet<string>?", "modifiedProperties") });

        if (_model.PropertyDependencies.Count > 0)
        {
            AppendLine();
            RenderExpandDependencies();
        }
    }

    private void RenderChangeAwareValidateBody()
    {
        AppendLine("var error = ValidationError.Valid;");
        AppendLine();

        if (_model.PropertyDependencies.Count > 0)
        {
            AppendLine("var propsToValidate = modifiedProperties is null");
            AppendLine("    ? null");
            AppendLine("    : ExpandDependencies(modifiedProperties);");
        }
        else
        {
            AppendLine("var propsToValidate = modifiedProperties;");
        }

        AppendLine();
        foreach (var prop in _model.Properties)
        {
            RenderChangeAwarePropertyValidation(prop);
            AppendLine();
        }
        AppendLine("return error;");
    }

    private void RenderChangeAwarePropertyValidation(PropertyValidationModel prop)
    {
        Comment($"Validate {prop.PropertyName}");
        AppendLine($"if (propsToValidate is null || propsToValidate.Contains(nameof({prop.PropertyName})))");
        Block(() => RenderPropertyValidationInner(prop));
    }

    private void RenderExpandDependencies()
    {
        // Static dependency graph — allocated once per type, O(1) lookup
        AppendLine("private static readonly global::System.Collections.Generic.Dictionary<string, string[]> __dependencyGraph = new()");
        AppendLine("{");
        IncreaseIndent();
        foreach (var dep in _model.PropertyDependencies)
        {
            var deps = string.Join(", ", dep.Dependents.Select(d => $"nameof({d})"));
            AppendLine($"[nameof({dep.Property})] = [{deps}],");
        }
        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        Method("ExpandDependencies", () =>
        {
            AppendLine("var expanded = new HashSet<string>(modified);");
            AppendLine("foreach (var prop in modified)");
            Block(() =>
            {
                AppendLine("if (__dependencyGraph.TryGetValue(prop, out var deps))");
                Block(() =>
                {
                    AppendLine("foreach (var dep in deps)");
                    IncreaseIndent();
                    AppendLine("expanded.Add(dep);");
                    DecreaseIndent();
                });
            });
            AppendLine("return expanded;");
        }, "HashSet<string>",
            new List<MethodParameter> { new("IReadOnlySet<string>", "modified") },
            AccessModifier.Private, new MethodModifiers { IsStatic = true });
    }

    #endregion

    #region Shared Property Validation

    private void RenderPropertyValidation(PropertyValidationModel prop)
    {
        Comment($"Validate {prop.PropertyName}");
        RenderPropertyValidationInner(prop);
    }

    private void RenderPropertyValidationInner(PropertyValidationModel prop)
    {
        var requiredAttrs = prop.Attributes.Where(a => a.Kind == ValidationKind.Required).ToList();
        var otherAttrs = prop.Attributes.Where(a => a.Kind != ValidationKind.Required).ToList();

        // ⚠️ Presence rules live OUTSIDE the presence guard, whichever guard it is. A guard says "only
        // when the value is there", and that is right for every rule that measures a value — asking
        // the minimum length of something absent means nothing, and it is [Required]'s job to say it
        // is missing. But a presence rule speaks about absence itself: inside the guard it ran only
        // once the value was already there, and its own check could never be true. [RequiredIf]
        // inside `is not null` never fired; [NotEmpty] inside the `else` of a `string.IsNullOrEmpty`
        // guard never fired either, and an empty string came out as validation.required. A rule that
        // cannot report the case it exists for.
        var presence = otherAttrs.Where(IsPresenceRule).ToList();
        var whenPresent = otherAttrs.Except(presence).ToList();

        var hasImplicitRequired = prop.IsRequiredModifier && requiredAttrs.Count == 0
            && (prop.IsNullable || prop.IsString || !IsNonNullableValueType(prop) || IsGuidType(prop.PropertyType));

        // A non-nullable value type (DateTime, int, enum, ...) can never be null, so a [Required]
        // presence guard is meaningless AND would emit invalid `is null` C# (CS0037). Guids keep
        // their meaningful Guid.Empty check. For these types we render the other attribute checks
        // directly, with no `if (... is null)` / `else` wrapper.
        var requiredEmitsGuard = !prop.IsNonNullableValueType || IsGuidType(prop.PropertyType);

        if ((requiredAttrs.Count > 0 || hasImplicitRequired) && requiredEmitsGuard)
        {
            foreach (var attr in presence)
                RenderAttributeValidation(prop, attr);

            var requiredAttr = requiredAttrs.Count > 0
                ? requiredAttrs[0]
                : new ValidationAttributeModel
                {
                    AttributeName = "Required",
                    AttributeType = "Pragmatic.Validation.Attributes.RequiredAttribute",
                    Kind = ValidationKind.Required
                };
            RenderRequiredValidation(prop, requiredAttr);

            if (whenPresent.Count > 0 || prop.ValidatesElements || prop.IsNestedValidatable)
            {
                AppendLine("else");
                Block(() => RenderWhenPresent(prop, whenPresent));
            }
        }
        else if (prop.IsNullable)
        {
            foreach (var attr in presence)
                RenderAttributeValidation(prop, attr);

            if (whenPresent.Count > 0 || prop.ValidatesElements || prop.IsNestedValidatable)
            {
                AppendLine($"if ({prop.PropertyName} is not null)");
                Block(() => RenderWhenPresent(prop, whenPresent));
            }
        }
        else
        {
            RenderWhenPresent(prop, otherAttrs);
        }
    }

    /// <summary>
    ///     The rules that speak about absence, and so cannot sit behind a guard that requires presence.
    /// </summary>
    private static bool IsPresenceRule(ValidationAttributeModel attr)
        => attr.Kind is ValidationKind.RequiredIf or ValidationKind.RequiredIfNot or ValidationKind.NotEmpty;

    private void RenderWhenPresent(PropertyValidationModel prop, List<ValidationAttributeModel> attrs)
    {
        foreach (var attr in attrs)
            RenderAttributeValidation(prop, attr);
        if (prop.ValidatesElements)
            RenderValidateElements(prop);
        if (prop.IsNestedValidatable)
            RenderValidateNestedObject(prop);
    }

    private void RenderAttributeValidation(PropertyValidationModel prop, ValidationAttributeModel attr)
    {
        switch (attr.Kind)
        {
            case ValidationKind.NotEmpty:
                RenderNotEmptyValidation(prop, attr);
                break;
            case ValidationKind.NotWhiteSpace:
                RenderNotWhiteSpaceValidation(prop, attr);
                break;
            case ValidationKind.MinLength:
                RenderMinLengthValidation(prop, attr);
                break;
            case ValidationKind.MaxLength:
                RenderMaxLengthValidation(prop, attr);
                break;
            case ValidationKind.Length:
                RenderLengthValidation(prop, attr);
                break;
            case ValidationKind.Email:
                RenderEmailValidation(prop, attr);
                break;
            case ValidationKind.Phone:
                RenderPhoneValidation(prop, attr);
                break;
            case ValidationKind.Url:
                RenderUrlValidation(prop, attr);
                break;
            case ValidationKind.Regex:
                RenderRegexValidation(prop, attr);
                break;
            case ValidationKind.CreditCard:
                RenderCreditCardValidation(prop, attr);
                break;
            case ValidationKind.Range:
                RenderRangeValidation(prop, attr);
                break;
            case ValidationKind.Positive:
                RenderPositiveValidation(prop, attr);
                break;
            case ValidationKind.Negative:
                RenderNegativeValidation(prop, attr);
                break;
            case ValidationKind.GreaterThan:
                RenderGreaterThanValidation(prop, attr);
                break;
            case ValidationKind.GreaterThanOrEqual:
                RenderGreaterThanOrEqualValidation(prop, attr);
                break;
            case ValidationKind.LessThan:
                RenderLessThanValidation(prop, attr);
                break;
            case ValidationKind.LessThanOrEqual:
                RenderLessThanOrEqualValidation(prop, attr);
                break;
            case ValidationKind.MinCount:
                RenderMinCountValidation(prop, attr);
                break;
            case ValidationKind.MaxCount:
                RenderMaxCountValidation(prop, attr);
                break;
            case ValidationKind.Count:
                RenderCountValidation(prop, attr);
                break;
            case ValidationKind.EqualTo:
                RenderEqualToValidation(prop, attr);
                break;
            case ValidationKind.NotEqualTo:
                RenderNotEqualToValidation(prop, attr);
                break;
            case ValidationKind.GreaterThanProperty:
                RenderGreaterThanPropertyValidation(prop, attr);
                break;
            case ValidationKind.LessThanProperty:
                RenderLessThanPropertyValidation(prop, attr);
                break;
            case ValidationKind.GreaterThanOrEqualProperty:
                RenderGreaterThanOrEqualPropertyValidation(prop, attr);
                break;
            case ValidationKind.LessThanOrEqualProperty:
                RenderLessThanOrEqualPropertyValidation(prop, attr);
                break;
            case ValidationKind.RequiredIf:
                RenderRequiredIfValidation(prop, attr);
                break;
            case ValidationKind.RequiredIfNot:
                RenderRequiredIfNotValidation(prop, attr);
                break;
            case ValidationKind.Guid:
                RenderGuidValidation(prop, attr);
                break;
            case ValidationKind.ValidEnum:
                RenderValidEnumValidation(prop, attr);
                break;
            case ValidationKind.FutureDate:
                RenderFutureDateValidation(prop, attr);
                break;
            case ValidationKind.PastDate:
                RenderPastDateValidation(prop, attr);
                break;
            case ValidationKind.OneOf:
                RenderOneOfValidation(prop, attr);
                break;
            default:
                RenderUnknownAttributeValidation(prop, attr);
                break;
        }
    }

    private static bool IsNonNullableValueType(PropertyValidationModel prop)
        => !prop.IsNullable && (prop.IsNumeric || prop.IsComparable) && !prop.IsString;

    private AccessModifier GetAccessModifier() => ToAccessModifier(_model.Accessibility);

    private static AccessModifier ToAccessModifier(string accessibility) => accessibility switch
    {
        "public" => AccessModifier.Public,
        "internal" => AccessModifier.Internal,
        "protected" => AccessModifier.Protected,
        "private" => AccessModifier.Private,
        _ => AccessModifier.Public
    };

    /// <summary>
    ///     The localization key as a C# expression: a literal when the generator knows the key, the
    ///     attribute instance's own <c>DefaultMessageKey</c> when it does not.
    /// </summary>
    /// <remarks>
    ///     A custom rule is re-instantiated by the generated code (<see cref="UnknownAttributeVariable" />),
    ///     so its key is one member access away. Falling through to a literal
    ///     <c>"validation.invalid"</c> would keep the key the rule declared from reaching the wire. An explicit
    ///     <c>MessageKey</c> is still a literal: the constructor arguments are copied to the new instance,
    ///     named arguments are not.
    /// </remarks>
    private static string MessageKeyExpression(ValidationAttributeModel attr)
    {
        if (!string.IsNullOrEmpty(attr.MessageKey))
            return $"\"{attr.MessageKey}\"";

        var known = ValidationMessageKeys.ForKind(attr.Kind);
        return known is not null
            ? $"\"{known}\""
            : $"{UnknownAttributeVariable(attr)}.DefaultMessageKey";
    }

    /// <summary>The local that holds a re-instantiated custom attribute in the generated code.</summary>
    private static string UnknownAttributeVariable(ValidationAttributeModel attr)
        => $"{attr.AttributeName.ToLowerInvariant()}Attr";

    /// <summary>
    ///     Renders the WithFor call with message key and optional validation parameters.
    ///     When severity is non-default (Warning/Info), emits the overload with severity argument.
    ///     When groups are specified, wraps the call in a group-check condition.
    /// </summary>
    private void RenderWithFor(PropertyValidationModel prop, ValidationAttributeModel attr, params string[] parameters)
    {
        var messageKey = MessageKeyExpression(attr);
        var hasSeverity = attr.Severity != 0;
        var hasGroups = !attr.Groups.IsDefaultOrEmpty;

        // When groups are specified, wrap in a group-check condition
        if (hasGroups)
        {
            var groupConditions = string.Join(" || ",
                attr.Groups.Select(g => $"group == \"{g}\""));
            AppendLine($"if (group is null || {groupConditions})");
            Block(() => RenderWithForInner(prop, messageKey, hasSeverity, attr.Severity, parameters));
        }
        else
        {
            RenderWithForInner(prop, messageKey, hasSeverity, attr.Severity, parameters);
        }
    }

    // messageKey is a C# expression (see MessageKeyExpression), not the key's text.
    private void RenderWithForInner(PropertyValidationModel prop, string messageKey, bool hasSeverity,
        int severity, string[] parameters)
    {
        var severityArg = severity switch
        {
            1 => "global::Pragmatic.Validation.Types.ValidationSeverity.Warning",
            2 => "global::Pragmatic.Validation.Types.ValidationSeverity.Info",
            _ => null
        };

        // ⚠️ Where [JsonPropertyName] renames the property, the issue is built in full to carry the
        // wire name too: it is the one half the response writer cannot derive — a naming policy
        // applies itself, an explicit rename does not. Publishing the C# name would send a generated
        // client looking for a key that is not in the map.
        if (!string.IsNullOrEmpty(prop.WireName))
        {
            var args = parameters.Length == 0 ? "" : ", " + string.Join(", ", parameters);
            var issue =
                $"new global::Pragmatic.Validation.Types.ValidationIssue({messageKey}, "
                + $"nameof({prop.PropertyName}){args}) {{ WirePath = \"{prop.WireName}\""
                + (severityArg is null ? "" : $", Severity = {severityArg}")
                + " }";
            AppendLine($"error = error.With({issue});");
            return;
        }

        if (!hasSeverity)
        {
            if (parameters.Length == 0)
                AppendLine($"error = error.WithFor(nameof({prop.PropertyName}), {messageKey});");
            else
            {
                var paramList = string.Join(", ", parameters);
                AppendLine($"error = error.WithFor(nameof({prop.PropertyName}), {messageKey}, {paramList});");
            }
        }
        else
        {
            if (parameters.Length == 0)
                AppendLine($"error = error.WithFor(nameof({prop.PropertyName}), {messageKey}, {severityArg});");
            else
            {
                var paramList = string.Join(", ", parameters);
                AppendLine($"error = error.WithFor(nameof({prop.PropertyName}), {messageKey}, {severityArg}, {paramList});");
            }
        }
    }

    #endregion
}
