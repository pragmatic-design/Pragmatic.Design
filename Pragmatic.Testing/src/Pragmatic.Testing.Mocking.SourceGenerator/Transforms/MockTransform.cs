using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.Testing.Mocking.SourceGenerator.Models;

namespace Pragmatic.Testing.Mocking.SourceGenerator.Transforms;

/// <summary>
///     Turns the interface named by <c>[GenerateMock&lt;T&gt;]</c> into a <see cref="MockModel"/>.
/// </summary>
/// <remarks>
///     Members come from the interface and everything it inherits: an explicit implementation has to
///     cover the full surface or the generated type does not compile. That is also why members the
///     mock cannot make configurable — overloads, generics — are still carried in the model, flagged
///     rather than dropped.
/// </remarks>
internal static class MockTransform
{
    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    ///     The same, without nullable annotations. Used for the signature of a generic method.
    /// </summary>
    /// <remarks>
    ///     <c>T?</c> on an <b>unconstrained</b> parameter cannot appear in an explicit interface
    ///     implementation: with no <c>class</c>/<c>struct</c> constraint the compiler cannot tell a
    ///     nullable reference from <c>Nullable&lt;T&gt;</c>, and rejects the member with CS0539 — "no
    ///     suitable member found to implement". This holds wherever the parameter appears, nested
    ///     included: <c>ValueTask&lt;T?&gt;</c> fails exactly like a bare <c>T?</c>, which is the shape
    ///     <c>ICacheStack.GetAsync</c> uses. Dropping annotations across a generic signature is the
    ///     only rendering that matches, and costs nothing on members that return <c>default</c>.
    /// </remarks>
    private static readonly SymbolDisplayFormat FullyQualifiedNoNullable =
        SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    ///     Builds the model, or returns null when <paramref name="type"/> is not an interface —
    ///     the caller reports PRAG2350 in that case.
    /// </summary>
    internal static MockModel? Build(INamedTypeSymbol type, string? nameOverride)
    {
        // A sealed class cannot be derived from, and a static one has nothing to derive.
        var isClass = type is { TypeKind: TypeKind.Class, IsSealed: false, IsStatic: false };

        if (type.TypeKind != TypeKind.Interface && !isClass)
            return null;

        var properties = new List<MockPropertyModel>();
        var methods = new List<MockMethodModel>();
        var events = new List<MockEventModel>();

        // Overloads are decided across the whole surface, so count names before shaping anything.
        var allMethods = AllMembers(type)
            .Where(m => m.Member is IMethodSymbol { MethodKind: MethodKind.Ordinary })
            .Select(m => (Method: (IMethodSymbol)m.Member, m.Declaring))
            .ToList();

        // Only NON-generic methods compete for a name. A generic overload is already unconfigurable
        // on its own account, so counting it would take the plain overload down with it — and
        // IDomainEventDispatcher, which has exactly that pair, would end up with nothing usable.
        var nameCounts = new Dictionary<string, int>();
        foreach (var (method, _) in allMethods.Where(m => !m.Method.IsGenericMethod && !m.Method.IsStatic))
            nameCounts[method.Name] = nameCounts.TryGetValue(method.Name, out var n) ? n + 1 : 1;

        foreach (var (member, declaring) in AllMembers(type))
        {
            if (member is IEventSymbol declaredEvent)
            {
                events.Add(new MockEventModel
                {
                    Name = declaredEvent.Name,
                    // Without the nullable annotation, because the template adds one. An interface
                    // declaring the handler nullable itself — SSH.NET's IBaseClient does — otherwise
                    // renders as `EventHandler<T>?? Name`, which does not parse, and every member
                    // after it in the file is silently lost with it.
                    Type = declaredEvent.Type.ToDisplayString(FullyQualifiedNoNullable),
                    DeclaringInterface = declaring.ToDisplayString(FullyQualified)
                });
                continue;
            }

            if (member is not IPropertySymbol property)
                continue;

            properties.Add(new MockPropertyModel
            {
                // An indexer has no name to hang a public member on, so it is implemented and left
                // unconfigurable — but implemented it must be, or the type does not compile.
                // IStringLocalizer is nothing but indexers.
                Name = property.Name,
                MemberName = MemberNameFor(property.Name, isClass),
                Type = property.Type.ToDisplayString(FullyQualified),
                HasGetter = property.GetMethod is not null,
                HasSetter = property.SetMethod is not null,
                DeclaringInterface = declaring.ToDisplayString(FullyQualified),
                IndexerParameters = property.Parameters
                    .Select(p => new MockParameterModel(p.Type.ToDisplayString(FullyQualified), Escape(p.Name), Modifier(p)))
                    .ToEquatableArray(),
                IsStatic = property.IsStatic
            });
        }

        // ONE place decides every public member name. Spread over several — overloads here, generics
        // and indexers in the template, hiding somewhere else — each new kind of member would
        // rediscover the same collisions the previous one had already solved.
        //
        // Non-generic methods claim their names first: IDomainEventDispatcher declares
        // DispatchAsync<TEvent> BEFORE the plain DispatchAsync, and in declaration order the generic
        // one would take the name, leaving the useful overload as DispatchAsync_2.
        var takenMemberNames = new HashSet<string>(StringComparer.Ordinal);
        var genericMembers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var indexer in properties.Where(p => p.IndexerParameters.Count > 0))
            takenMemberNames.Add($"Item{indexer.IndexerParameters.Count}");

        // Properties claim their names too; otherwise a type declaring both a property and a method of
        // the same name would emit two members with one name.
        foreach (var property in properties.Where(p => p.IndexerParameters.Count == 0 && !p.IsStatic))
            takenMemberNames.Add(property.MemberName);

        foreach (var (method, declaring) in allMethods.OrderBy(m => m.Method.IsGenericMethod ? 1 : 0))
        {
            var isGeneric = method.IsGenericMethod;

            // A static member has no instance to configure against, and claiming a name for it would
            // push the instance member of the same name aside for nothing.
            if (method.IsStatic)
            {
                methods.Add(StaticMethod(method, declaring));
                continue;
            }

            var isOverload = !isGeneric && nameCounts[method.Name] > 1;

            // An overload gets the parameter count appended, so each gets its own configurable
            // member instead of all of them losing one. Arity alone is not enough — IConnectionMultiplexer
            // overloads GetServer twice at the same count — so a suffix breaks any remaining tie.
            // A generic method whose name a plain overload already took becomes {Name}Generic, rather
            // than being skipped and leaving tests with nothing to assert against.
            string memberName;

            if (isGeneric)
            {
                // Generic overloads SHARE one member — it is keyed by type argument, not by
                // signature, so ICacheStack's two GetOrSetAsync need exactly one. The suffix is only
                // to step aside when a plain overload already holds the name.
                if (!genericMembers.TryGetValue(method.Name, out memberName!))
                {
                    var plain = MemberNameFor(method.Name, isClass);
                    memberName = takenMemberNames.Contains(plain) ? $"{plain}Generic" : plain;
                    genericMembers[method.Name] = memberName;
                    takenMemberNames.Add(memberName);
                }
            }
            else
            {
                memberName = MemberNameFor(
                    isOverload ? $"{method.Name}{method.Parameters.Length}" : method.Name,
                    isClass);

                if (!takenMemberNames.Add(memberName))
                {
                    var n = 2;
                    string candidate;
                    do
                    {
                        candidate = $"{memberName}_{n++}";
                    } while (!takenMemberNames.Add(candidate));

                    memberName = candidate;
                }
            }

            var format = isGeneric ? FullyQualifiedNoNullable : FullyQualified;

            methods.Add(new MockMethodModel
            {
                Name = method.Name,
                MemberName = memberName,
                ReturnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(format),
                IsVoid = method.ReturnsVoid,
                Parameters = method.Parameters
                    .Select(p => new MockParameterModel(p.Type.ToDisplayString(format), Escape(p.Name), Modifier(p)))
                    .ToEquatableArray(),
                // Every non-generic method is configurable: within the typed arities through a typed
                // member, beyond them through MockWideMethod. Nothing is left unreachable — except a
                // by-reference parameter, which cannot be carried in a typed member or boxed into an
                // argument array, so the method is implemented and left alone.
                Configurable = !isGeneric && !HasByRefParameter(method),
                IsWide = !isGeneric && !HasByRefParameter(method)
                         && method.Parameters.Length > MaxConfigurableArity(method),
                TypeParameters = method.TypeParameters
                    .Select(t => t.Name)
                    .ToEquatableArray(),
                DeclaringInterface = declaring.ToDisplayString(FullyQualified),
                DefaultResult = DefaultResultFor(method.ReturnType),
                IsStatic = false
            });
        }

        return new MockModel
        {
            InterfaceFullName = type.ToDisplayString(FullyQualified),
            InterfaceShortName = type.Name,
            ClassName = nameOverride ?? DefaultClassName(type),
            Properties = properties.ToEquatableArray(),
            Methods = methods.ToEquatableArray(),
            Events = events.ToEquatableArray(),
            IsClass = isClass
        };
    }

    /// <summary>
    ///     <c>IClock</c> becomes <c>ClockMock</c>; a name that does not follow the <c>I</c> convention
    ///     keeps its own, so <c>Clock</c> would become <c>ClockMock</c> too.
    /// </summary>
    /// <remarks>
    ///     A closed generic keeps its type arguments in the name — <c>IHandler&lt;Evt&gt;</c> becomes
    ///     <c>HandlerOfEvtMock</c>. Dropping them would give two different instantiations of the same
    ///     generic one class name, and the second would silently overwrite the first.
    /// </remarks>
    internal static string DefaultClassName(INamedTypeSymbol type)
    {
        var baseName = Strip(type.Name);

        if (type.TypeArguments.Length > 0)
            baseName += "Of" + string.Join(string.Empty, type.TypeArguments.Select(a => Strip(a.Name)));

        return NamingHelper.AppendSuffix(baseName, "Mock");
    }

    private static string Strip(string name) =>
        name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1])
            ? name.Substring(1)
            : name;

    /// <summary>
    ///     Prefixes <c>@</c> when a parameter is named after a C# keyword.
    /// </summary>
    /// <remarks>
    ///     <c>IDomainEventHandler.HandleAsync(TEvent @event, …)</c> is declared with an escaped
    ///     keyword, but <see cref="ISymbol.Name"/> gives back the bare <c>event</c>. Emitted as-is the
    ///     compiler reads the keyword, the member is parsed as an event declaration, and the errors
    ///     that surface (CS0065, CS0102 on a member with an empty name) point nowhere near the cause.
    /// </remarks>
    private static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;

    /// <summary>
    ///     A completed task for methods returning <c>Task</c>, so an unconfigured call does not hand
    ///     the caller a null to await.
    /// </summary>
    /// <remarks>
    ///     <c>ValueTask</c> and <c>ValueTask&lt;T&gt;</c> need nothing: their default value is already
    ///     a completed one. <c>Task</c> and <c>Task&lt;T&gt;</c> are reference types whose default is
    ///     null, and <c>await null</c> throws a NullReferenceException from inside the code under
    ///     test — pointing anywhere except at the mock that was never configured.
    /// </remarks>
    private static string? DefaultResultFor(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol named)
            return null;

        var name = named.ConstructedFrom.ToDisplayString(FullyQualifiedNoNullable);

        return name switch
        {
            "global::System.Threading.Tasks.Task" =>
                "global::System.Threading.Tasks.Task.CompletedTask",
            "global::System.Threading.Tasks.Task<TResult>" =>
                $"global::System.Threading.Tasks.Task.FromResult<{named.TypeArguments[0].ToDisplayString(FullyQualified)}>(default!)",
            _ => null
        };
    }

    /// <summary>Whether any parameter is passed by reference, which rules the method out of configuration.</summary>
    private static bool HasByRefParameter(IMethodSymbol method) =>
        method.Parameters.Any(p => p.RefKind is RefKind.Ref or RefKind.Out);

    /// <summary>The <c>ref</c>/<c>out</c>/<c>in</c> keyword a parameter carries, or empty.</summary>
    private static string Modifier(IParameterSymbol parameter) => parameter.RefKind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        _ => string.Empty
    };

    /// <summary>
    ///     The public member's name: the member's own for an interface, <c>{Name}Setup</c> for a class.
    /// </summary>
    /// <remarks>
    ///     Deriving from a class means the <c>override</c> already carries the member's name, so the
    ///     configurable member needs one of its own — <c>Blob.UploadAsyncSetup.Returns(…)</c> against
    ///     <c>Blob.UploadAsync</c>, which is what the system under test calls.
    /// </remarks>
    private static string MemberNameFor(string name, bool isClass) => isClass ? name + "Setup" : name;

    /// <summary>
    ///     A <c>static abstract</c> method: implemented so the class compiles, configurable by nobody.
    /// </summary>
    /// <remarks>
    ///     The return type is rendered without nullable annotations for the same reason a generic
    ///     signature is, and it must not reach a type argument: an interface with unimplemented
    ///     <c>static abstract</c> members cannot be one (CS8920), and <c>IAmazonService</c> — which
    ///     is both the declarer here and the return type — is exactly that interface.
    /// </remarks>
    private static MockMethodModel StaticMethod(IMethodSymbol method, INamedTypeSymbol declaring) =>
        new()
        {
            Name = method.Name,
            MemberName = string.Empty,
            ReturnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(FullyQualifiedNoNullable),
            IsVoid = method.ReturnsVoid,
            Parameters = method.Parameters
                .Select(p => new MockParameterModel(p.Type.ToDisplayString(FullyQualifiedNoNullable), Escape(p.Name), Modifier(p)))
                .ToEquatableArray(),
            Configurable = false,
            IsWide = false,
            TypeParameters = method.TypeParameters.Select(t => t.Name).ToEquatableArray(),
            DeclaringInterface = declaring.ToDisplayString(FullyQualified),
            DefaultResult = null,
            IsStatic = true
        };

    /// <summary>
    ///     The runtime carries mock methods up to four parameters (one for void). Beyond that the
    ///     member is implemented but not configurable, rather than referencing a type that does not exist.
    /// </summary>
    private static int MaxConfigurableArity(IMethodSymbol method) => method.ReturnsVoid ? 3 : 5;

    /// <summary>
    ///     The interface's own members plus every inherited one, each paired with the interface that
    ///     <b>declares</b> it.
    /// </summary>
    /// <remarks>
    ///     The pairing is what an explicit implementation needs. <c>IServiceScope : IDisposable</c>
    ///     inherits <c>Dispose</c>, but writing <c>void IServiceScope.Dispose()</c> does not compile —
    ///     the member must be qualified with <c>IDisposable</c>, the interface that declares it.
    /// </remarks>
    private static IEnumerable<(ISymbol Member, INamedTypeSymbol Declaring)> AllMembers(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Class)
        {
            // Walking the base chain by hand, because only the most derived declaration of a
            // signature counts. Every declaration is recorded, not just the overridable ones: a
            // derived class can hide a virtual member with a plain `new` one — BlobClient does this
            // to four of BlobBaseClient's — and overriding the base declaration through the hiding
            // one is CS0506.
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object;
                 current = current.BaseType)
            foreach (var member in current.GetMembers())
                if (seen.Add(Signature(member)) && Overridable(member))
                    yield return (member, current);

            yield break;
        }

        foreach (var member in type.GetMembers())
            yield return (member, type);

        foreach (var inherited in type.AllInterfaces)
        foreach (var member in inherited.GetMembers())
            yield return (member, inherited);
    }

    /// <summary>
    ///     Whether a class member can be overridden by the mock: public, virtual or abstract, and not
    ///     already sealed.
    /// </summary>
    /// <remarks>
    ///     <c>ToString</c> and friends are excluded even though they are virtual. Overriding them
    ///     gains nothing, and the public member exposing one would hide the <see cref="object"/>
    ///     member it shadows — CS0108, an error under warnings-as-errors.
    /// </remarks>
    private static bool Overridable(ISymbol member) =>
        member is { DeclaredAccessibility: Accessibility.Public, IsSealed: false }
        && (member.IsVirtual || member.IsAbstract || member.IsOverride)
        && member.Name is not ("ToString" or "Equals" or "GetHashCode")
        && member is not IMethodSymbol { MethodKind: not MethodKind.Ordinary };

    /// <summary>Name plus parameter types — what decides whether two declarations are the same member.</summary>
    private static string Signature(ISymbol member) => member switch
    {
        IMethodSymbol method =>
            method.Name + "(" + string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(FullyQualified))) + ")",
        IPropertySymbol { Parameters.Length: > 0 } indexer =>
            "this[" + string.Join(",", indexer.Parameters.Select(p => p.Type.ToDisplayString(FullyQualified))) + "]",
        _ => member.Name
    };
}
