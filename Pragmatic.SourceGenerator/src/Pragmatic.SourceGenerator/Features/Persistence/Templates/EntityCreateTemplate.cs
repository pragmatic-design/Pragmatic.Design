using System;
using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating static Create factory method on entities.
///     The Create method takes required parameters and returns a new entity instance.
/// </summary>
internal sealed class EntityCreateTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public EntityCreateTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Entity] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Create", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        // Abstract entities cannot have a Create() factory — derived types handle creation
        return _model is { IsValid: true, IsAbstract: false };
    }

    public override void RenderFile()
    {
        AddUsing("System");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Factory methods for creating {_model.TypeName} instances.");

        // ICreatable<T> on every entity, because a parameterless Create() is now emitted for every
        // entity — see RenderBody. Before, the interface held for the two entities out of fifty-three
        // whose factory happened to take no parameters, which made `where T : ICreatable<T>` a promise
        // that was true almost nowhere.
        var interfaces = new List<string>
        {
            $"global::Pragmatic.Persistence.Entity.ICreatable<{_model.TypeName}>"
        };

        Class(_model.TypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true },
            interfaces: interfaces);
    }

    private void RenderBody()
    {
        RenderCreateMethod();

        var hasRequiredProps = _model.Properties.Any(p => p.IsRequiredForCreate);

        // ⚠️ A parameterless overload for every entity, and it is what makes construction uniform.
        //
        // The alternative is for the mutation invoker and ToEntity to call Create(a, b) — which they
        // cannot see, so they would have to PREDICT its signature: a second copy of "what is required
        // at creation", kept in step by a test. Emitting this overload removes the thing to predict
        // instead of predicting it better, and the caller's question collapses to "is it an entity",
        // which is an attribute the author wrote and every generator can read.
        //
        // It does not make an incomplete entity valid: the required properties stay at their default
        // until the caller writes them, exactly as `new` leaves them. What it adds over `new` is the
        // rest — the key, [DefaultValue], [ComputedDefault] and the audit stamps — which `new` skips
        // and nothing downstream puts back.
        if (hasRequiredProps)
        {
            AppendLine();
            XmlSummary($"Creates a new {_model.TypeName} with only the values the generator supplies.");

            var parameters = new List<MethodParameter>();

            // ⚠️ The same optional TimeProvider the other overload takes, when the entity is auditable.
            // The body is shared, and it stamps CreatedAt from `timeProvider` — without the parameter
            // that is a CS0103 inside a generated file, which is how this was caught.
            if (_model.IsAuditable)
            {
                XmlParam("timeProvider", "Optional TimeProvider for audit timestamps. Falls back to DateTimeOffset.UtcNow if null.");
                parameters.Add(new MethodParameter("global::System.TimeProvider?", "timeProvider") { DefaultValue = "null" });
            }

            XmlReturns($"A new {_model.TypeName} instance.");

            Method("Create", () => RenderCreateBody([]),
                $"global::{_model.FullTypeName}",
                parameters, AccessModifier.Public,
                new MethodModifiers { IsStatic = true });
        }

        // An auditable entity's parameterless call is Create(timeProvider: null), whose signature is
        // not the interface's Create() — so the explicit implementation bridges the two, whether or
        // not there are required properties.
        if (_model.IsAuditable)
        {
            AppendLine();
            XmlSummary("Explicit interface implementation — delegates to Create(timeProvider: null).");
            AppendLine(
                $"static {_model.TypeName} global::Pragmatic.Persistence.Entity.ICreatable<{_model.TypeName}>.Create() => Create();");
        }
    }

    private void RenderCreateMethod()
    {
        // Get required properties (for method parameters)
        var requiredProps = _model.Properties
            .Where(p => p.IsRequiredForCreate)
            .ToList();

        // Build XML documentation
        XmlSummary($"Creates a new {_model.TypeName} instance with required properties.");

        foreach (var prop in requiredProps)
        {
            XmlParam(ToCamelCase(prop.Name), $"The {ToHumanReadable(prop.Name)}.");
        }

        if (_model.IsAuditable)
        {
            XmlParam("timeProvider", "Optional TimeProvider for audit timestamps. Falls back to DateTimeOffset.UtcNow if null.");
        }

        XmlReturns($"A new {_model.TypeName} instance.");

        // Build parameters
        var parameters = requiredProps
            .Select(p => new MethodParameter(NormalizeTypeName(p.TypeName), ToCamelCase(p.Name)))
            .ToList();

        // Add optional TimeProvider parameter for auditable entities
        if (_model.IsAuditable)
        {
            parameters.Add(new MethodParameter("global::System.TimeProvider?", "timeProvider") { DefaultValue = "null" });
        }

        Method("Create", () => RenderCreateBody(requiredProps),
            $"global::{_model.FullTypeName}",
            parameters, AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }

    private void RenderCreateBody(List<PropertyMetadataModel> requiredProps)
    {
        AppendLine($"return new global::{_model.FullTypeName}");
        AppendLine("{");
        IncreaseIndent();

        // Always set PersistenceId
        RenderIdInitializer();

        // Set required properties from parameters
        foreach (var prop in requiredProps)
        {
            AppendLine($"{prop.Name} = {ToCamelCase(prop.Name)},");
        }

        // Set audit properties if applicable
        if (_model.IsAuditable)
        {
            RenderAuditInitializers();
        }

        // Set soft delete properties if applicable
        if (_model.IsSoftDelete)
        {
            RenderSoftDeleteInitializers();
        }

        // Set static default values from [DefaultValue(...)]
        RenderDefaultValueInitializers();

        // Set the state machine's entry state from [InitialState]
        RenderInitialStateInitializer(requiredProps);

        // And whatever the compiler will not let us leave out.
        RenderRequiredMemberPlaceholders(requiredProps);

        DecreaseIndent();
        AppendLine("};");
    }

    /// <summary>
    ///     Names every C# <c>required</c> member the initializer has not already assigned.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ An object initializer that omits a <c>required</c> member is <b>CS9035</b>, so an
    ///         entity written the modern way could not be declared <c>[Entity]</c> at all: the error
    ///         landed inside the generated factory, on code the author cannot edit. Found declaring
    ///         the five entities of <c>Pragmatic.Authorization.Management</c>, every one of which
    ///         carries <c>required</c> members.
    ///     </para>
    ///     <para>
    ///         <c>default!</c> and not an invented value: the property is left exactly where <c>new</c>
    ///         would have left it, which is what the parameterless overload promises. The two cases it
    ///         covers are the parameterless overload — where nothing is asked for — and a
    ///         <c>required</c> member that is nullable, which the factory does not ask for because it
    ///         can be absent.
    ///     </para>
    /// </remarks>
    private void RenderRequiredMemberPlaceholders(List<PropertyMetadataModel> requiredProps)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal) { "PersistenceId" };

        foreach (var prop in requiredProps)
            assigned.Add(prop.Name);

        if (_model.IsAuditable)
        {
            assigned.Add("CreatedAt");
            assigned.Add("UpdatedAt");
        }

        if (_model.IsSoftDelete)
            assigned.Add("IsDeleted");

        foreach (var prop in _model.Properties.Where(p => p.DefaultValueExpression is not null))
            assigned.Add(prop.Name);

        if (!string.IsNullOrEmpty(_model.StateMachinePropertyName))
            assigned.Add(_model.StateMachinePropertyName!);

        foreach (var prop in _model.Properties)
        {
            if (!prop.IsRequiredMember || assigned.Contains(prop.Name))
                continue;

            AppendLine($"{prop.Name} = default!,");
        }
    }

    /// <summary>
    ///     Assigns the value marked <c>[InitialState]</c> to the property the state machine governs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Nothing did this. <c>[InitialState]</c> was read by <c>StateMachineTransform</c>,
    ///         checked by <c>StateMachineValidator</c> — <c>PRAG0620</c> announcing that the factory
    ///         "cannot set a default state" — and consumed by no template at all. A new entity took the
    ///         enum's numeric zero, and both reference applications marked their first declared value,
    ///         so zero and the intended state coincided and the gap left no trace.
    ///     </para>
    ///     <para>
    ///         Skipped where the caller or another declaration already writes the property: a duplicate
    ///         member in an object initializer is a compile error, and between two declarations the more
    ///         specific one — the parameter, or <c>[DefaultValue]</c> on that very property — is the one
    ///         the author wrote last about this entity.
    ///     </para>
    /// </remarks>
    private void RenderInitialStateInitializer(List<PropertyMetadataModel> requiredProps)
    {
        var property = _model.StateMachinePropertyName;
        var expression = _model.StateMachineInitialStateExpression;

        if (string.IsNullOrEmpty(property) || string.IsNullOrEmpty(expression))
            return;

        if (requiredProps.Any(p => p.Name == property))
            return;

        if (_model.Properties.Any(p => p.Name == property && p.DefaultValueExpression is not null))
            return;

        // The property has to exist to be assigned; PRAG0623 covers the case where it does not, and
        // emitting the line anyway would bury that diagnostic under a CS0117 in a generated file.
        if (!_model.Properties.Any(p => p.Name == property))
            return;

        AppendLine($"{property} = {expression},");
    }

    /// <summary>
    ///     Assigns the key, for the one id type the factory can assign.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The key is ours to produce because it is a <c>Guid</c>, always: <c>IEntity</c> is not
    ///         generic and <c>[Entity]</c> carries no key type. Numeric and string keys need no branch
    ///         here, since neither can be declared.
    ///     </para>
    /// </remarks>
    private void RenderIdInitializer()
        => AppendLine("PersistenceId = global::System.Guid.CreateVersion7(),");

    private void RenderAuditInitializers()
    {
        Comment("Audit timestamps: use TimeProvider if available, otherwise fall back to UtcNow");
        AppendLine("CreatedAt = timeProvider?.GetUtcNow() ?? global::System.DateTimeOffset.UtcNow,");
        AppendLine("UpdatedAt = timeProvider?.GetUtcNow() ?? global::System.DateTimeOffset.UtcNow,");
    }

    private void RenderSoftDeleteInitializers()
    {
        AppendLine("IsDeleted = false,");
    }

    private void RenderDefaultValueInitializers()
    {
        var defaultProps = _model.Properties
            .Where(p => p.DefaultValueExpression is not null)
            .ToList();

        if (defaultProps.Count == 0)
            return;

        foreach (var prop in defaultProps)
        {
            AppendLine($"{prop.Name} = {prop.DefaultValueExpression},");
        }
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string ToHumanReadable(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Insert spaces before uppercase letters (e.g. "FirstName" → "first name")
        return string.Concat(name.SelectMany((c, i) =>
            i > 0 && char.IsUpper(c)
                ? new[] { ' ', char.ToLowerInvariant(c) }
                : new[] { char.ToLowerInvariant(c) }));
    }

    /// <summary>
    ///     Normalizes type name for code generation.
    ///     Primitive C# aliases are kept as-is, while other types get global:: prefix.
    /// </summary>
    private static string NormalizeTypeName(string typeName)
    {
        // C# primitive type aliases should not have global:: prefix
        return typeName switch
        {
            "string" or "String" or "System.String" => "string",
            "int" or "Int32" or "System.Int32" => "int",
            "long" or "Int64" or "System.Int64" => "long",
            "short" or "Int16" or "System.Int16" => "short",
            "byte" or "Byte" or "System.Byte" => "byte",
            "sbyte" or "SByte" or "System.SByte" => "sbyte",
            "uint" or "UInt32" or "System.UInt32" => "uint",
            "ulong" or "UInt64" or "System.UInt64" => "ulong",
            "ushort" or "UInt16" or "System.UInt16" => "ushort",
            "float" or "Single" or "System.Single" => "float",
            "double" or "Double" or "System.Double" => "double",
            "decimal" or "Decimal" or "System.Decimal" => "decimal",
            "bool" or "Boolean" or "System.Boolean" => "bool",
            "char" or "Char" or "System.Char" => "char",
            "object" or "Object" or "System.Object" => "object",
            "Guid" or "System.Guid" => "global::System.Guid",
            _ when typeName.Contains(".") => $"global::{typeName}",
            _ => typeName
        };
    }
}
