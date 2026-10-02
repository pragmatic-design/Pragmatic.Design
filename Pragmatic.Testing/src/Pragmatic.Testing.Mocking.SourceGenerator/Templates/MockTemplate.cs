using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.Testing.Mocking.SourceGenerator.Models;

namespace Pragmatic.Testing.Mocking.SourceGenerator.Templates;

/// <summary>
///     Emits the mock class for one <c>[GenerateMock&lt;T&gt;]</c> declaration.
/// </summary>
/// <remarks>
///     Every member appears twice. Publicly, under the interface member's own name, as a
///     <c>MockProperty</c>/<c>MockMethod</c> the test configures and asserts on. Explicitly, as the
///     interface member the system under test calls, delegating to the public one. The explicit
///     implementation is what makes the two share a name.
/// </remarks>
internal sealed class MockTemplate : CSharpTemplate
{
    private const string MockNs = "global::Pragmatic.Testing.Mocking";

    private readonly MockModel _model;

    public MockTemplate(MockModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.Testing.Mocking.SourceGenerator";

    public override Artifact RenderOutput() => new($"Mock.{_model.ClassName}.g.cs", ToSourceText());

    protected override bool Validate() => !string.IsNullOrWhiteSpace(_model.ClassName);

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Tests.Generated;");
        AppendLine();
        // A generic signature is rendered WITHOUT nullable annotations, because `T?` on an
        // unconstrained parameter cannot appear in an explicit implementation (CS0539). The compiler
        // then objects that the annotations do not match (CS8616) — the two rules cannot both be
        // satisfied, and CS8616 is the one with no consequence on a member returning default.
        AppendLine("#pragma warning disable CS8616, CS8769 // nullability of generic signatures, see above");
        // A mock implements every member of the interface, including any the library marks
        // experimental or obsolete — and their analyzers then object to code the author never wrote.
        // StackExchange.Redis raises SER006 across IDatabase; the list grows as other SDKs are mocked.
        AppendLine("#pragma warning disable SER001, SER002, SER003, SER004, SER005, SER006 // StackExchange.Redis experimental APIs");
        AppendLine("#pragma warning disable CS0618, CS0612 // obsolete members must still be implemented");
        AppendLine();
        // `<see cref="global::Foo"/>` is not valid XML documentation — the `::` breaks the parser and
        // surfaces as a wall of CS1570 pointing at the comment rather than at anything real.
        AppendLine($"/// <summary>Generated mock for <c>{_model.InterfaceShortName}</c>.</summary>");
        if (_model.IsClass)
            AppendLine("/// <remarks>Derives from the mocked class: only its virtual members can be "
                       + "configured, each through a <c>…Setup</c> member.</remarks>");
        AppendLine($"public sealed class {_model.ClassName} : {_model.InterfaceFullName}");
        AppendLine("{");
        IncreaseIndent();

        RenderConfigurableMembers();
        RenderExplicitImplementations();

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderConfigurableMembers()
    {
        foreach (var property in _model.Properties.Where(p => p.IndexerParameters.Count == 0 && !p.IsStatic))
        {
            AppendLine($"/// <summary>Configures and records <c>{_model.InterfaceShortName}.{property.Name}</c>.</summary>");
            AppendLine($"public {MockNs}.MockProperty<{property.Type}> {property.MemberName} {{ get; }} = "
                       + $"new(\"{_model.InterfaceShortName}.{property.Name}\");");
            AppendLine();
        }

        // An indexer is configured through a member named after its arity — IStringLocalizer, whose
        // whole surface is `this[key]`, is unusable otherwise.
        foreach (var indexer in _model.Properties.Where(p => p.IndexerParameters.Count is > 0 and <= 4))
        {
            var types = indexer.IndexerParameters.Select(p => p.Type).ToList();
            types.Add(indexer.Type);

            AppendLine($"/// <summary>Configures and records the <c>{_model.InterfaceShortName}</c> indexer.</summary>");
            AppendLine($"public {MockNs}.MockMethod<{string.Join(", ", types)}> {IndexerMember(indexer)} {{ get; }} = "
                       + $"new(\"{_model.InterfaceShortName}[…]\");");
            AppendLine();
        }

        // A generic method gets a member keyed by type argument — the only part of the call that can
        // decide the result before the caller closes it. Unless a plain overload already claimed the
        // name, as on IDomainEventDispatcher: two members cannot share it, and the plain one is the
        // more useful of the pair.
        var emittedGenerics = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var method in _model.Methods
                     .Where(m => m.TypeParameters.Count == 1 && !m.IsStatic)
                     // ICacheStack overloads GetOrSetAsync twice, both generic: they share one member.
                     .Where(m => emittedGenerics.Add(m.MemberName)))
        {
            AppendLine($"/// <summary>Configures and records <c>{_model.InterfaceShortName}.{method.Name}</c>, per type argument.</summary>");
            // The default comes from the model too, so a Task-returning generic does not hand back
            // null — the same defect the non-generic members were seeded against.
            //
            // ⚠️ Unless the default names the method's own type parameter. The member is a property of
            // the mock, not of the method, so `Task.FromResult<IReadOnlyList<TResult>>(default!)` in
            // its initialiser is a CS0246 on `TResult` — an error inside a file the author cannot
            // edit, and the whole mock is lost with it. Found the first time a mocked interface grew a
            // generic method returning a constructed type (IReadRepository.RunAsync). Such a member is
            // left unseeded: the test that closes it says what it answers.
            var seed = method.DefaultResult is { } generic && !NamesATypeParameter(generic, method)
                ? $", {generic}"
                : string.Empty;

            AppendLine($"public {Hiding(method.MemberName)}{MockNs}.MockGenericMethod {method.MemberName} {{ get; }} = "
                       + $"new(\"{_model.InterfaceShortName}.{method.Name}\"{seed});");
            AppendLine();
        }

        foreach (var method in _model.Methods.Where(m => m.Configurable))
        {
            var memberType = MockMemberType(method);

            // A Task-returning member is seeded with a completed task: its default is null, and an
            // unconfigured call would fail inside the caller's await rather than at the mock.
            // The type has to be spelled out when something is chained — a target-typed `new` cannot
            // be inferred through a method call (CS8754).
            // A wide member takes its default through the constructor; a typed one chains .Returns,
            // which needs the type spelled out because a target-typed `new` cannot be inferred
            // through a method call (CS8754).
            var initialiser = method switch
            {
                { IsWide: true, DefaultResult: { } wide } =>
                    $"new(\"{_model.InterfaceShortName}.{method.Name}\", {wide})",
                { DefaultResult: { } value } =>
                    $"new {memberType}(\"{_model.InterfaceShortName}.{method.Name}\").Returns({value})",
                _ => $"new(\"{_model.InterfaceShortName}.{method.Name}\")"
            };

            AppendLine($"/// <summary>Configures and records <c>{_model.InterfaceShortName}.{method.Name}</c>.</summary>");
            AppendLine($"public {Hiding(method.MemberName)}{memberType} {method.MemberName} {{ get; }} = {initialiser};");
            AppendLine();
        }
    }

    /// <summary>
    ///     Whether a default-result expression mentions one of the method's own type parameters.
    /// </summary>
    /// <remarks>
    ///     A word-boundary match rather than a substring one: <c>T</c> must not fire on
    ///     <c>Task</c>, and <c>TResult</c> must fire on <c>IReadOnlyList&lt;TResult&gt;</c>.
    /// </remarks>
    private static bool NamesATypeParameter(string expression, Models.MockMethodModel method)
        => method.TypeParameters.Any(parameter =>
            System.Text.RegularExpressions.Regex.IsMatch(
                expression, $@"\b{System.Text.RegularExpressions.Regex.Escape(parameter)}\b"));

    private void RenderExplicitImplementations()
    {
        foreach (var declaredEvent in _model.Events)
        {
            // Empty accessors: the interface cannot be implemented without them, and raising a
            // mocked event is not something anything here needs.
            AppendLine($"{Overriding()}event {declaredEvent.Type}? "
                       + $"{Qualifier(declaredEvent.DeclaringInterface)}{declaredEvent.Name}");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("add { }");
            AppendLine("remove { }");
            DecreaseIndent();
            AppendLine("}");
            AppendLine();
        }

        foreach (var property in _model.Properties)
        {
            var isIndexer = property.IndexerParameters.Count > 0;

            var accessors = new List<string>();
            if (isIndexer)
            {
                var args = string.Join(", ", property.IndexerParameters.Select(p => p.Name));
                var configurable = property.IndexerParameters.Count <= 4;

                if (property.HasGetter)
                    accessors.Add(configurable
                        ? $"get => {IndexerMember(property)}.Invoke({args});"
                        : $"get => default({property.Type})!;");
                if (property.HasSetter) accessors.Add("set { }");
            }
            else
            {
                if (property.HasGetter) accessors.Add($"get => {property.MemberName}.Get();");
                if (property.HasSetter) accessors.Add($"set => {property.MemberName}.Set(value);");
            }

            var name = isIndexer
                ? "this[" + string.Join(", ", property.IndexerParameters.Select(p => $"{p.Type} {p.Name}")) + "]"
                : property.Name;

            if (property.IsStatic)
            {
                accessors.Clear();
                if (property.HasGetter) accessors.Add($"get => {Unsupported(property.Name)}");
                if (property.HasSetter) accessors.Add($"set => {Unsupported(property.Name)}");
            }

            AppendLine($"{Static(property.IsStatic)}{Overriding()}{property.Type} {Qualifier(property.DeclaringInterface)}{name}");
            AppendLine("{");
            IncreaseIndent();
            foreach (var accessor in accessors)
                AppendLine(accessor);
            DecreaseIndent();
            AppendLine("}");
            AppendLine();
        }

        foreach (var method in _model.Methods)
        {
            var typeParams = method.TypeParameters.Count > 0
                ? "<" + string.Join(", ", method.TypeParameters) + ">"
                : string.Empty;

            var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Modifier}{p.Type} {p.Name}"));
            var arguments = string.Join(", ", method.Parameters.Select(p => p.Name));
            var outParameters = method.Parameters.Where(p => p.Modifier == "out ").ToList();

            AppendLine($"{Static(method.IsStatic)}{Overriding()}{method.ReturnType} "
                       + $"{Qualifier(method.DeclaringInterface)}{method.Name}{typeParams}({parameters})");

            // An `out` parameter must be assigned before the method returns, so the body cannot be an
            // expression. These methods are never configurable — see MockTransform.HasByRefParameter.
            if (outParameters.Count > 0)
            {
                AppendLine("{");
                IncreaseIndent();
                foreach (var parameter in outParameters)
                    AppendLine($"{parameter.Name} = default({parameter.Type})!;");
                if (!method.IsVoid)
                    AppendLine($"return default({method.ReturnType})!;");
                DecreaseIndent();
                AppendLine("}");
                AppendLine();
                continue;
            }

            IncreaseIndent();

            if (method.IsStatic)
                AppendLine($"=> {Unsupported(method.Name)}");
            else if (method.IsWide)
                AppendLine(method.IsVoid
                    ? $"=> {method.MemberName}.InvokeVoid({arguments});"
                    : $"=> {method.MemberName}.Invoke<{method.ReturnType}>({arguments});");
            else if (method.Configurable)
                AppendLine($"=> {method.MemberName}.Invoke({arguments});");
            else if (method.TypeParameters.Count == 1)
                AppendLine(method.IsVoid
                    ? $"=> {method.MemberName}.InvokeVoid<{method.TypeParameters[0]}>({arguments});"
                    : $"=> {method.MemberName}.Invoke<{method.TypeParameters[0]}, {method.ReturnType}>({arguments});");
            else
                // Not configurable — overloaded, generic, or wider than the runtime's arities. It is
                // still implemented so the type compiles; PRAG2351/PRAG2352 told the author why.
                AppendLine(method.IsVoid ? "{ }" : $"=> default({method.ReturnType})!;");

            DecreaseIndent();
            AppendLine();
        }
    }

    /// <summary>The <c>static</c> keyword an explicit implementation of a static member needs.</summary>
    private static string Static(bool isStatic) => isStatic ? "static " : string.Empty;

    /// <summary>
    ///     <c>public override </c> when deriving from a class; nothing when implementing an interface,
    ///     where the member is implemented explicitly and takes no accessibility at all.
    /// </summary>
    private string Overriding() => _model.IsClass ? "public override " : string.Empty;

    /// <summary>
    ///     The <c>IFoo.</c> an explicit implementation is qualified with; empty for an override, which
    ///     is qualified by nothing.
    /// </summary>
    private string Qualifier(string declaringType) => _model.IsClass ? string.Empty : declaringType + ".";

    /// <summary>
    ///     The body of a <c>static abstract</c> member: it throws, rather than returning default like
    ///     an unconfigured instance member, because no test can configure it into doing something else.
    /// </summary>
    private string Unsupported(string memberName) =>
        $"throw new global::System.NotSupportedException("
        + $"\"{_model.InterfaceShortName}.{memberName} is static; a mock cannot stand in for it.\");";

    /// <summary>
    ///     <c>new </c> when the member's name collides with one inherited from <see cref="object"/>.
    /// </summary>
    /// <remarks>
    ///     Some interfaces redeclare <c>ToString</c> — <c>IConnectionMultiplexer</c> does. The public
    ///     mock member then hides <c>object.ToString()</c>, which is CS0108 and, under
    ///     warnings-as-errors, a build failure. Hiding is exactly what is meant here.
    /// </remarks>
    private static string Hiding(string memberName) =>
        memberName is "ToString" or "Equals" or "GetHashCode" or "GetType"
            ? "new "
            : string.Empty;

    /// <summary>
    ///     The member name for an indexer: <c>Item</c>, suffixed by its parameter count when a type
    ///     declares more than one — <c>IStringLocalizer</c> has both <c>this[key]</c> and
    ///     <c>this[key, args]</c>.
    /// </summary>
    private static string IndexerMember(MockPropertyModel indexer) =>
        $"Item{indexer.IndexerParameters.Count}";

    /// <summary>The runtime type backing a configurable member, chosen by return kind and arity.</summary>
    private static string MockMemberType(MockMethodModel method)
    {
        // Past the typed arities the member is driven by the boxed argument list — see MockWideMethod.
        if (method.IsWide)
            return $"{MockNs}.MockWideMethod";

        var parameterTypes = method.Parameters.Select(p => p.Type).ToList();

        if (method.IsVoid)
            return parameterTypes.Count == 0
                ? $"{MockNs}.MockVoidMethod"
                : $"{MockNs}.MockVoidMethod<{string.Join(", ", parameterTypes)}>";

        parameterTypes.Add(method.ReturnType);
        return $"{MockNs}.MockMethod<{string.Join(", ", parameterTypes)}>";
    }
}
