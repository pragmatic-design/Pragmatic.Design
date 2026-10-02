using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Generates the boundary interface(s) and local implementation.
///     Produces: public interface, internal interface (always), and local impl class.
///     DI registration is handled directly by the host via convention-derived type names.
///     Supports both DomainAction and Mutation members via BoundaryMemberModel.
/// </summary>
internal sealed partial class BoundaryInterfaceTemplate : CSharpTemplate
{
    private readonly BoundaryModel _boundary;
    private readonly ImmutableArray<BoundaryMemberModel> _publicMembers;
    private readonly ImmutableArray<BoundaryMemberModel> _internalMembers;
    private readonly ImmutableArray<PackageActionRegistration> _packageRegistrations;
    private readonly bool _hasInternalMembers;
    private readonly bool _hasSubBoundaries;
    private readonly BoundaryOutputMode _mode;
    private readonly bool _hasRemote;

    public BoundaryInterfaceTemplate(
        BoundaryModel boundary,
        ImmutableArray<BoundaryMemberModel> publicMembers,
        ImmutableArray<BoundaryMemberModel> internalMembers,
        ImmutableArray<PackageActionRegistration> packageRegistrations = default,
        BoundaryOutputMode mode = BoundaryOutputMode.Definition,
        bool hasRemote = true)
    {
        _boundary = boundary;
        _publicMembers = publicMembers;
        _internalMembers = internalMembers;
        _packageRegistrations = packageRegistrations.IsDefault
            ? ImmutableArray<PackageActionRegistration>.Empty
            : packageRegistrations;
        _hasInternalMembers = !internalMembers.IsEmpty;
        _hasSubBoundaries = boundary.HasSubBoundaries;
        _mode = mode;
        _hasRemote = hasRemote;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_boundary.InterfaceName} from {_boundary.Namespace}";
    protected override string? TriggerInfo => $"[Boundary] on {_boundary.TypeName}";

    public override Artifact RenderOutput()
    {
        var suffix = _mode switch
        {
            BoundaryOutputMode.Definition => "Definition",
            BoundaryOutputMode.Local => "Local",
            BoundaryOutputMode.Remote => "Remote",
            _ => "Interface"
        };
        return new(VirtualFolderHints.ForBoundary(_boundary.TypeName, suffix), ToSourceText());
    }

    protected override bool Validate()
        => _boundary.IsValid && (!_publicMembers.IsEmpty || !_internalMembers.IsEmpty || _hasSubBoundaries || !_packageRegistrations.IsEmpty);

    public override void RenderFile()
    {
        switch (_mode)
        {
            case BoundaryOutputMode.Definition:
                RenderDefinitionFile();
                break;
            case BoundaryOutputMode.Local:
                RenderLocalFile();
                break;
            case BoundaryOutputMode.Remote:
                RenderRemoteFile();
                break;
        }
    }

    // ── Definition: interfaces + DI extension (the switch) ──

    private void RenderDefinitionFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_boundary.Namespace);
        AppendLine();

        // Sub-boundary interfaces
        if (_hasSubBoundaries && !_boundary.IsInternal)
        {
            foreach (var sub in _boundary.SubBoundaries)
            {
                RenderSubPublicInterface(sub);
                AppendLine();

                if (sub.HasInternalTwin)
                {
                    RenderSubInternalInterface(sub);
                    AppendLine();
                }
            }
        }

        // Public interface
        if (!_boundary.IsInternal)
        {
            RenderPublicInterface();
            AppendLine();
        }

        // Internal interface
        RenderInternalInterface();

        AppendLine();

        // DI extension with mode switch
        RenderDiExtension();
    }

    // ── Local: local impl class + sub-boundary local impls ──

    private void RenderLocalFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_boundary.Namespace);
        AppendLine();

        // Sub-boundary local implementations — the internal twin, and the guarded one the public
        // sub-interface resolves to.
        if (_hasSubBoundaries)
        {
            foreach (var sub in _boundary.SubBoundaries)
            {
                RenderSubLocalImplementation(sub);
                AppendLine();

                if (!_boundary.IsInternal)
                {
                    RenderSubGuardedImplementation(sub);
                    AppendLine();
                }
            }
        }

        // Root local implementation
        RenderLocalImplementation();

        AppendLine();

        if (!_boundary.IsInternal)
        {
            RenderGuardedImplementation();
            AppendLine();
        }

        // AddLocal private method as partial extension
        RenderLocalRegistrationPartial();
    }

    // ── Remote: remote impl class ──

    private void RenderRemoteFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_boundary.Namespace);
        AppendLine();

        // Inline error record (self-contained, no Composition.Host dependency)
        RenderRemoteErrorRecord();
        AppendLine();

        // Remote implementation
        if (!_boundary.IsInternal)
        {
            RenderRemoteImplementation();
            AppendLine();
        }

        // AddRemote private method as partial extension
        RenderRemoteRegistrationPartial();
    }

    private void RenderLocalRegistrationPartial()
    {
        var className = $"{_boundary.TypeName}Extensions";

        Class(className, () => RenderAddLocalMethod(),
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    private void RenderRemoteRegistrationPartial()
    {
        var className = $"{_boundary.TypeName}Extensions";

        Class(className, () => RenderAddRemoteMethod(),
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string DeriveMethodName(BoundaryMemberModel member)
    {
        var suffix = member switch
        {
            { IsQuery: true } => "Query",
            { IsMutation: true } => "Mutation",
            _ => "Action"
        };
        var typeName = member.TypeName;
        return typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - suffix.Length)
            : typeName;
    }

    private static string DeriveFieldName(BoundaryMemberModel member)
        => $"_{TemplateHelpers.ToCamelCase(DeriveMethodName(member))}";

    private static string DeriveParamName(BoundaryMemberModel member)
        => TemplateHelpers.ToCamelCase(DeriveMethodName(member));

    private static string GetMethodReturnType(BoundaryMemberModel member)
    {
        if (member.IsVoid)
            return "global::System.Threading.Tasks.Task<global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>>";

        var returnType = member.ReturnTypeName ?? "object";
        return $"global::System.Threading.Tasks.Task<global::Pragmatic.Result.Result<{returnType}, global::Pragmatic.Result.IError>>";
    }

    private static string GetInvokerInterfaceType(BoundaryMemberModel member)
    {
        if (member.IsMutation)
            return $"global::Pragmatic.Actions.Invoker.IMutationInvoker<{member.FullTypeName}, {member.EntityFullTypeName}>";

        if (member.IsVoid)
            return $"global::Pragmatic.Actions.Invoker.IVoidDomainActionInvoker<{member.FullTypeName}>";

        var returnType = member.ReturnTypeName ?? "object";
        return $"global::Pragmatic.Actions.Invoker.IDomainActionInvoker<{member.FullTypeName}, {returnType}>";
    }

    private static string StripBoundarySuffix(string typeName)
    {
        const string suffix = "Boundary";
        return typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - suffix.Length)
            : typeName;
    }

    private static string StripGlobalPrefix(string fullTypeName)
    {
        const string prefix = "global::";
        return fullTypeName.StartsWith(prefix, StringComparison.Ordinal)
            ? fullTypeName.Substring(prefix.Length)
            : fullTypeName;
    }

    private static ImmutableArray<ActionPropertyModel> OrderProperties(
        ImmutableArray<ActionPropertyModel> properties)
        => properties.OrderBy(p => IsOptionalParam(p) ? 1 : 0).ToImmutableArray();

    // A default the parameter list cannot carry does not make the parameter optional either, or the
    // signature would declare an optional parameter with nothing after the '='.
    private static bool IsOptionalParam(ActionPropertyModel p)
        => (p.HasDefaultValue && p.DefaultIsCompileTimeConstant)
           || p is { IsNullable: true, IsRequired: false };

    /// <summary>
    ///     The default to write after <c>=</c>, or <c>null</c> when the parameter takes none.
    /// </summary>
    /// <remarks>
    ///     A property initialiser is an expression and a parameter default is a compile-time constant,
    ///     so the two do not always agree. <c>= []</c> on a <c>List&lt;string&gt;</c> is an ordinary
    ///     way to write "starts empty" and produced <c>List&lt;string&gt; aliases = []</c> here —
    ///     CS1736, inside a generated file the author cannot edit. Such a parameter becomes required,
    ///     which <c>OrderProperties</c> already places before the optional ones; the initialiser still
    ///     reaches the request body record, where it is legal and where dropping it would turn the
    ///     default into a null.
    /// </remarks>
    private static string? GetDefaultValueForParam(ActionPropertyModel p)
    {
        if (p.HasDefaultValue)
            return p.DefaultIsCompileTimeConstant ? p.DefaultValueSyntax : null;
        if (p is { IsNullable: true, IsRequired: false })
            return "null";
        return null;
    }

    private static string GetParamTypeName(ActionPropertyModel p)
    {
        var typeName = p.TypeName;
        if (p.IsNullable && !typeName.EndsWith("?", StringComparison.Ordinal))
            return typeName + "?";
        return typeName;
    }
}
