using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Determines whether a type symbol represents a DI service dependency (as opposed to
///     a value/DTO property). Used by Action, Mutation, Endpoint, and Validation transforms
///     to distinguish constructor-injected services from user-facing properties.
/// </summary>
/// <remarks>
///     <para>
///         Every rule here is a semantic one. The previous implementation matched
///         <c>ToDisplayString().Contains("DbContext")</c>, which promoted any user type whose <em>name</em>
///         merely contained the substring (e.g. a domain class <c>InvoiceDbContextLogger</c>) to a
///         dependency — and demoted every non-Microsoft concrete service to a DTO property. Getting this
///         wrong means a user property disappears from the generated DTO, or a dependency ends up in the
///         HTTP body.
///     </para>
///     <para>
///         Concrete reference types that carry no signal are reported as
///         <see cref="ServiceTypeClassification.Ambiguous" /> instead of being silently bucketed: there is
///         no structural difference between <c>HttpClient</c> and a user's concrete model class.
///     </para>
/// </remarks>
internal static class ServiceTypeDetector
{
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";

    /// <summary>
    ///     Class-level attributes that declare a concrete type <em>is</em> a DI service.
    ///     Fully-qualified, matched on the generic definition so <c>[Service&lt;T&gt;]</c> is covered.
    /// </summary>
    private static readonly string[] DiRegistrationAttributes =
    [
        "Pragmatic.Composition.Attributes.ServiceAttribute",
        "Pragmatic.Composition.Attributes.DecoratorAttribute",
        "Pragmatic.Composition.Attributes.FactoryAttribute",
        "Pragmatic.Composition.Attributes.ServiceFactoryAttribute"
    ];

    /// <summary>
    ///     Returns true if the type is a DI service dependency. Ambiguous concrete types resolve to
    ///     <c>false</c> (data) — the historical, safe-for-DTOs default. Call
    ///     <see cref="Classify(ITypeSymbol)" /> where the ambiguity must be surfaced to the user.
    /// </summary>
    public static bool IsServiceType(ITypeSymbol type) => Classify(type) == ServiceTypeClassification.Service;

    /// <summary>
    ///     Classifies a member's type, resolving one case <see cref="Classify(ITypeSymbol)" /> cannot:
    ///     a boundary interface of <paramref name="assembly" /> itself.
    /// </summary>
    /// <param name="type">The member's type.</param>
    /// <param name="assembly">The assembly being compiled, whose boundaries name the interfaces below.</param>
    /// <remarks>
    ///     <para>
    ///         <c>I{Boundary}Actions</c>, <c>I{Boundary}InternalActions</c> and a sub-boundary's
    ///         <c>I{Boundary}{Group}Actions</c> are emitted later in this same compilation, and a
    ///         generator never sees its own output — so while a field is being classified the symbol is
    ///         an <b>error type</b>: not an interface, no attributes, no base chain, nothing to read but
    ///         the name. It therefore falls to <see cref="ServiceTypeClassification.Ambiguous" />, and
    ///         the consequence is not just a diagnostic: the field is <b>not injected</b>, the warning
    ///         does not stop the build, and the operation throws on its first call.
    ///     </para>
    ///     <para>
    ///         ⚠️ Matching a name is what the repository's rules warn against, and the exception is kept
    ///         narrow deliberately. The names come from a catalogue built out of the boundaries this
    ///         generator is about to emit, not from a convention imposed on user code; and the question
    ///         is asked <b>only</b> of a symbol that failed to resolve, so it can never override what a
    ///         real type says about itself — it only replaces "no idea".
    ///     </para>
    ///     <para>
    ///         The shape is matched rather than the exact name because sub-boundary interfaces are named
    ///         after folders this classifier does not know. Being a little wide costs little: a name
    ///         that matches but that nothing emits produces a generated constructor asking for a type
    ///         that does not exist — a compile error naming it, which is louder than the silent null it
    ///         replaces.
    ///     </para>
    /// </remarks>
    public static ServiceTypeClassification Classify(ITypeSymbol type, IAssemblySymbol? assembly)
    {
        var classification = Classify(type);
        return classification == ServiceTypeClassification.Ambiguous
               && QualifiedGeneratedService(type, assembly) is not null
            ? ServiceTypeClassification.Service
            : classification;
    }

    /// <summary>
    ///     The fully qualified name of the service this unresolved symbol names, when it is one this
    ///     generator writes into <paramref name="assembly" />: a boundary interface, or the resolver of a
    ///     <c>[PragmaticUser]</c> entity. Null otherwise.
    /// </summary>
    public static string? QualifiedGeneratedService(ITypeSymbol type, IAssemblySymbol? assembly)
        => QualifiedBoundaryInterface(type, assembly) ?? QualifiedUserResolver(type, assembly);

    /// <summary>
    ///     The fully qualified name of the <c>{User}Resolver</c> this unresolved symbol names, or null.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same position as the boundary interfaces above: the resolver is written by the Identity
    ///         feature of this same generator, so while a field is classified it is an error type. The name
    ///         is recognised because a <c>[PragmaticUser]</c> entity of this assembly produces it
    ///         (<c>NamingHelper.AppendSuffix(entity, "Resolver")</c>), and it is qualified with that
    ///         entity's namespace — where the resolver lands.
    ///     </para>
    ///     <para>
    ///         Public entities only: that is the condition the resolver is registered on
    ///         (<c>IdentityFeature.ResolverServices</c>), and a field the container cannot fill is better
    ///         reported than injected with nothing behind it.
    ///     </para>
    /// </remarks>
    public static string? QualifiedUserResolver(ITypeSymbol type, IAssemblySymbol? assembly)
    {
        const string Suffix = "Resolver";

        if (type.TypeKind != TypeKind.Error || assembly is null)
            return null;

        var name = type.Name;
        if (!name.EndsWith(Suffix, StringComparison.Ordinal))
            return null;

        // AppendSuffix does not double a suffix: an entity already named "…Resolver" keeps its name.
        var user = FindPublicUserEntity(assembly.GlobalNamespace, name.Substring(0, name.Length - Suffix.Length))
                   ?? FindPublicUserEntity(assembly.GlobalNamespace, name);
        if (user is null)
            return null;

        return user.ContainingNamespace.IsGlobalNamespace
            ? $"global::{name}"
            : $"global::{user.ContainingNamespace.ToDisplayString()}.{name}";
    }

    private static INamedTypeSymbol? FindPublicUserEntity(INamespaceSymbol ns, string typeName)
    {
        if (typeName.Length == 0)
            return null;

        foreach (var candidate in ns.GetTypeMembers(typeName))
        {
            if (candidate.DeclaredAccessibility == Accessibility.Public
                && candidate.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeNames.PragmaticUser))
                return candidate;
        }

        foreach (var nested in ns.GetNamespaceMembers())
        {
            var found = FindPublicUserEntity(nested, typeName);
            if (found is not null)
                return found;
        }

        return null;
    }

    /// <summary>
    ///     The fully qualified name of the boundary interface this unresolved symbol names, or null.
    /// </summary>
    /// <remarks>
    ///     ⚠️ One reading for the two consumers. The symbol is an error type, so
    ///     <c>ToDisplayString(FullyQualifiedFormat)</c> hands back the bare name the author wrote; the
    ///     catalogue that recognised it is also the only thing that knows where the interface will
    ///     land. Recognising here and qualifying somewhere else is how the two came apart, and the
    ///     result bound by accident wherever the operation's namespace nested under the boundary's.
    /// </remarks>
    /// <summary>
    ///     Whether the unresolved symbol names the boundary facade that is emitted <c>internal</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Answered here, beside the recognition, because it is the same reading. The internal
    ///     facade is <c>I{Boundary}InternalActions</c> — <c>BoundaryTransform.DeriveInternalInterfaceName</c>
    ///     inserts <c>Internal</c> before the trailing word — and it cannot be asked for its
    ///     accessibility: at this point it is an error type, because this same generator has not
    ///     emitted it yet. Recognising it in one place and deciding what to do with it in another is
    ///     how the two would come apart.
    /// </remarks>
    public static bool NamesTheInternalBoundaryFacade(ITypeSymbol type, IAssemblySymbol? assembly)
        => QualifiedBoundaryInterface(type, assembly) is not null
           && type.Name.EndsWith("InternalActions", StringComparison.Ordinal);

    public static string? QualifiedBoundaryInterface(ITypeSymbol type, IAssemblySymbol? assembly)
    {
        if (type.TypeKind != TypeKind.Error)
            return null;

        var name = type.Name;
        if (name.Length < 3 || name[0] != 'I' || !name.EndsWith("Actions", StringComparison.Ordinal))
            return null;

        foreach (var boundary in BoundaryOwnershipReader.BoundaryNamesOf(assembly))
        {
            var shortName = boundary.ShortName;
            if (shortName.Length > 0
                && name.Length > shortName.Length + 1
                && string.CompareOrdinal(name, 1, shortName, 0, shortName.Length) == 0)
                return string.IsNullOrEmpty(boundary.Namespace)
                    ? $"global::{name}"
                    : $"global::{boundary.Namespace}.{name}";
        }

        return null;
    }

    /// <summary>
    ///     Classifies a member's type as data, a DI service, or undecidable.
    /// </summary>
    public static ServiceTypeClassification Classify(ITypeSymbol type)
    {
        // Value types, string, arrays, delegates and pointers are never DI dependencies.
        if (type.IsValueType)
            return ServiceTypeClassification.Data;
        if (type.SpecialType is SpecialType.System_String or SpecialType.System_Object)
            return ServiceTypeClassification.Data;
        if (type.TypeKind is TypeKind.Array or TypeKind.Delegate or TypeKind.Pointer
            or TypeKind.Dynamic or TypeKind.TypeParameter)
            return ServiceTypeClassification.Data;

        // A collection of data is data — IReadOnlyList<Guid>, IEnumerable<string>, ICollection<int>.
        // Checked before the interface rule below, which would otherwise take them for dependencies:
        // an action declaring `public required IReadOnlyList<Guid> Ids { get; init; }` had the property
        // dropped from the request body, and the generated endpoint then emitted `new TheAction()`
        // and failed to compile with CS9035 on generated code that names neither the property nor why.
        //
        // The element type decides, so IEnumerable<IActionFilter> stays a dependency: what makes a
        // collection a service is what it holds, not that it is one.
        if (IsCollectionOfData(type))
            return ServiceTypeClassification.Data;

        // Interfaces are always DI dependencies (IRepository<T>, ILogger<T>, ICurrentUser, IMemoryCache…),
        // whoever implements them.
        if (type.TypeKind == TypeKind.Interface)
            return ServiceTypeClassification.Service;

        // Abstract classes are DI dependencies (DbContext bases, MutationInvoker base, DbConnection…).
        if (type is INamedTypeSymbol { IsAbstract: true })
            return ServiceTypeClassification.Service;

        // A real EF Core DbContext — established by walking the base chain, not by name matching.
        if (InheritsFromDbContext(type))
            return ServiceTypeClassification.Service;

        // The user declared it a service: [Service], [Service<T>], [Decorator], [Factory].
        if (HasDiRegistrationAttribute(type))
            return ServiceTypeClassification.Service;

        // Framework-owned concrete types are services, never user data. The base chain is walked so a
        // third-party concrete service still lands here through its framework base — NpgsqlConnection
        // is not in a Microsoft namespace but derives from System.Data.Common.DbConnection.
        if (DerivesFromFrameworkType(type))
            return ServiceTypeClassification.Service;

        // A concrete reference type with no signal: undecidable here.
        return ServiceTypeClassification.Ambiguous;
    }

    /// <summary>
    ///     Whether the type is a BCL collection whose element is not itself a service.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only the well-known generic collection interfaces of <c>System.Collections.Generic</c>:
    ///         a user interface that happens to derive from <c>IEnumerable&lt;T&gt;</c> is still a
    ///         dependency, and treating it as data would put it in a request body.
    ///     </para>
    ///     <para>
    ///         The element decides, and what disqualifies it is being a <b>service</b> — not failing to
    ///         prove it is data. A first version asked for <c>Data</c>, which covers
    ///         <c>IReadOnlyList&lt;Guid&gt;</c> and leaves out <c>IReadOnlyList&lt;SomeDto&gt;</c>: a
    ///         record classifies as <c>Ambiguous</c>, because for a <i>direct</i> property a concrete
    ///         type carries no signal. For an element it does — a list of a concrete type is a list of
    ///         values, and a list of DTOs in a request body is the most ordinary shape there is. Both
    ///         halves of this were found by a consumer application, one after the other, each as a
    ///         CS9035 on generated code naming neither the property nor the cause.
    ///     </para>
    /// </remarks>
    private static bool IsCollectionOfData(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named)
            return false;

        var definition = named.OriginalDefinition.ToDisplayString();
        if (definition is not (
            "System.Collections.Generic.IReadOnlyList<T>"
            or "System.Collections.Generic.IReadOnlyCollection<T>"
            or "System.Collections.Generic.IList<T>"
            or "System.Collections.Generic.ICollection<T>"
            or "System.Collections.Generic.IEnumerable<T>"
            or "System.Collections.Generic.ISet<T>"
            or "System.Collections.Generic.IReadOnlySet<T>"))
            return false;

        return Classify(named.TypeArguments[0]) != ServiceTypeClassification.Service;
    }

    private static bool InheritsFromDbContext(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString() == DbContextMetadataName)
                return true;
        }
        return false;
    }

    private static bool HasDiRegistrationAttribute(ITypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            var attrClass = attr.AttributeClass?.OriginalDefinition;
            if (attrClass is null)
                continue;

            // ServiceAttribute<TInterface> displays as "…ServiceAttribute<TInterface>"; compare on the
            // unconstructed name so both the generic and non-generic forms match.
            var name = attrClass.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? $"{ns.ToDisplayString()}.{attrClass.Name}"
                : attrClass.Name;

            foreach (var known in DiRegistrationAttributes)
            {
                if (name == known)
                    return true;
            }
        }
        return false;
    }

    private static bool DerivesFromFrameworkType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_Object)
                return false;
            if (IsFrameworkNamespace(current))
                return true;
        }
        return false;
    }

    /// <summary>
    ///     True for types owned by the framework rather than by the user: everything under
    ///     <c>Microsoft.*</c>, plus the <c>System.*</c> sub-namespaces that exist to hold services
    ///     (HTTP clients, ADO.NET providers). Data-shaped <c>System</c> types (<c>System.Uri</c>,
    ///     <c>System.String</c>) are deliberately not covered.
    /// </summary>
    private static bool IsFrameworkNamespace(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
            return false;

        var nsName = ns.ToDisplayString();

        if (nsName == "Microsoft" || nsName.StartsWith("Microsoft.", StringComparison.Ordinal))
            return true;

        return nsName is "System.Net.Http" or "System.Data.Common" or "System.Data"
            || nsName.StartsWith("System.Net.Http.", StringComparison.Ordinal)
            || nsName.StartsWith("System.Data.", StringComparison.Ordinal);
    }
}
