using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Renders the local implementation class and the DI extension method for boundary registration.
/// </summary>
internal sealed partial class BoundaryInterfaceTemplate
{
    // =========================================================================
    // Local Implementation
    // =========================================================================

    /// <summary>
    ///     The implementation behind the <b>internal</b> interface: a call from one operation of this
    ///     boundary to another of the same boundary, which the boundary already answered for.
    /// </summary>
    private void RenderLocalImplementation()
    {
        // Always implement the internal interface (which extends the public one)
        var implementedInterface = _boundary.InternalInterfaceName;

        XmlSummary(
            $"Local in-process implementation of <see cref=\"{_boundary.InternalInterfaceName}\"/>. Delegates to invokers as an internal call.");

        Class(_boundary.ImplementationName, () => RenderLocalImplBody(guarded: false),
            interfaces: new List<string> { implementedInterface },
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    /// <summary>
    ///     The implementation behind the <b>public</b> interface — the one another module injects.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One class serving both interfaces would enter an internal call on every method. The
    ///         public interface is not the intra-boundary path: it is the cross-boundary contract, and
    ///         entering an internal call there means exactly "do not ask for permissions" —
    ///         <c>IsInternalCall</c> is read by the authorization filters and by nothing else. A module
    ///         would obtain another's writes by injecting its interface.
    ///     </para>
    ///     <para>
    ///         Two classes rather than one with a flag: the scope is the only line that differs, and
    ///         which behaviour applies is decided where the container is built, not on each call.
    ///     </para>
    /// </remarks>
    private void RenderGuardedImplementation()
    {
        XmlSummary(
            $"Local in-process implementation of <see cref=\"{_boundary.InterfaceName}\"/> for callers outside this module. The invoked operation's own permission is enforced.");

        Class(GuardedImplementationName(_boundary.ImplementationName), () => RenderLocalImplBody(guarded: true),
            interfaces: new List<string> { _boundary.InterfaceName },
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    /// <summary>The name of the guarded twin of an implementation class.</summary>
    private static string GuardedImplementationName(string implementationName)
        => implementationName.EndsWith("Actions", StringComparison.Ordinal)
            ? implementationName.Substring(0, implementationName.Length - "Actions".Length) + "GuardedActions"
            : implementationName + "Guarded";

    private void RenderLocalImplBody(bool guarded)
    {
        // Boundary-level members (excludes members in sub-boundaries). The guarded twin implements the
        // public interface only, so it carries the public members and nothing else.
        var allMembers = (guarded ? _publicMembers : _publicMembers.AddRange(_internalMembers))
            .OrderBy(m => m.TypeName)
            .ToList();

        // ⚠️ No invoker is injected here — not for actions, not for mutations, not for queries. With
        // one constructor parameter per operation, an operation that is a member of its own boundary
        // and injects that boundary's facade closes a cycle: building the facade builds that
        // operation's invoker, whose own construction asks for the facade again. Microsoft's
        // container detects cycles by walking constructor parameters, so with the invoker resolving
        // the facade from a provider inside its body the cycle is invisible and the container simply
        // stops answering — a request that hangs rather than an error.
        // Resolving at the call breaks it by construction: nothing an operation needs is built while
        // the facade is being built. Queries work this way too, for a different reason.
        Field("_services", "global::System.IServiceProvider", AccessModifier.Private, isReadOnly: true);

        // Fields — sub-boundary interfaces
        if (_hasSubBoundaries)
        {
            // The concrete twin, not the interface: both twins implement the sub-interface, and the
            // container would otherwise hand the guarded one to the internal root as well.
            foreach (var sub in _boundary.SubBoundaries)
                Field($"_{TemplateHelpers.ToCamelCase(sub.PropertyName)}", SubImplFor(sub, guarded), AccessModifier.Private, isReadOnly: true);
        }

        // Field — call context for internal call scoping. The guarded twin never enters one, and an
        // assigned-but-unread private field is CS0414 under --warnaserror.
        if (!guarded)
            Field("_callContext", "global::Pragmatic.Pipeline.ICallContext?", AccessModifier.Private, isReadOnly: true);

        AppendLine();

        // Constructor — the provider, the sub-interfaces, and the optional call context
        var ctorParams = new List<MethodParameter>
        {
            new("global::System.IServiceProvider", "services"),
        };

        if (_hasSubBoundaries)
        {
            foreach (var sub in _boundary.SubBoundaries)
                ctorParams.Add(new MethodParameter(SubImplFor(sub, guarded), TemplateHelpers.ToCamelCase(sub.PropertyName)));
        }

        // ICallContext is optional — null when not registered (e.g., no authorization)
        if (!guarded)
            ctorParams.Add(new MethodParameter("global::Pragmatic.Pipeline.ICallContext?", "callContext") { DefaultValue = "null" });

        Constructor(guarded ? GuardedImplementationName(_boundary.ImplementationName) : _boundary.ImplementationName, () =>
        {
            AppendLine("_services = services;");

            if (_hasSubBoundaries)
            {
                foreach (var sub in _boundary.SubBoundaries)
                {
                    var fieldName = $"_{TemplateHelpers.ToCamelCase(sub.PropertyName)}";
                    var paramName = TemplateHelpers.ToCamelCase(sub.PropertyName);
                    AppendLine($"{fieldName} = {paramName};");
                }
            }

            if (!guarded)
                AppendLine("_callContext = callContext;");
        }, ctorParams, AccessModifier.Public);

        // Sub-boundary property getters
        if (_hasSubBoundaries)
        {
            foreach (var sub in _boundary.SubBoundaries)
            {
                var field = $"_{TemplateHelpers.ToCamelCase(sub.PropertyName)}";

                XmlInheritDoc();

                // ⚠️ Two properties of the same name and different types when the group has an internal
                // twin, and both are needed. The internal interface declares it as the twin and the
                // public one as the public type; C# resolves an interface implementation by exact type,
                // so the second is written explicitly. One object answers both.
                if (!guarded && sub.HasInternalTwin)
                {
                    AppendLine($"public {sub.InternalInterfaceName} {sub.PropertyName} => {field};");
                    AppendLine($"{sub.InterfaceName} {_boundary.InterfaceName}.{sub.PropertyName} => {field};");
                }
                else
                {
                    AppendLine($"public {sub.InterfaceName} {sub.PropertyName} => {field};");
                }

                AppendLine();
            }
        }

        // Methods — boundary-level only
        foreach (var member in allMembers)
            RenderLocalImplMethod(member, entersInternalCall: !guarded);
    }

    /// <summary>The concrete sub-boundary implementation a root of this kind holds.</summary>
    private static string SubImplFor(SubBoundaryModel sub, bool guarded)
        => guarded ? GuardedImplementationName(sub.ImplementationName) : sub.ImplementationName;

    /// <summary>
    ///     Returns the invoker's result — through the mutation's projection when it declares a
    ///     <c>ReturnType</c> other than the entity. The invoker always returns the entity: persistence,
    ///     events and the endpoint's <c>Location</c> need it, and only the member's answer changes.
    /// </summary>
    private void RenderInvokerReturn(BoundaryMemberModel member, string awaitedInvocation)
    {
        if (member.MutationResultProjection is { } projection)
        {
            AppendLine($"var __result = await {awaitedInvocation};");
            AppendLine($"return __result.Map({projection});");
            return;
        }

        AppendLine($"return await {awaitedInvocation};");
    }

    private void RenderLocalImplMethod(BoundaryMemberModel member, bool entersInternalCall)
    {
        var methodName = DeriveMethodName(member);
        var returnType = GetMethodReturnType(member);
        var paramName = MemberParamName(member);

        // DTO overload — delegates to invoker within internal call scope
        XmlInheritDoc();
        Method(methodName, () =>
        {
            if (entersInternalCall)
            {
                // Intra-boundary: the caller is an operation of this same boundary, already authorized
                // for it. Authorization filters skip internal calls, so the invoked operation's own
                // permission is not asked again. The guarded twin omits this line, and that is the
                // whole difference between the two.
                AppendLine("using var __scope = _callContext?.EnterInternalCall();");
            }

            if (member.IsQuery)
            {
                // The query's own invoker: validation, permission, then the read — the same pipeline the
                // route goes through, which is the point of naming the query here at all.
                AppendLine($"return await new {member.FullTypeName}.Invoker(_services).RunAsync({paramName}, ct).ConfigureAwait(false);");
            }
            else
            {
                // Resolved at the call, never in the constructor: see the field. The type is a literal
                // this generator writes and registers, and GetRequiredService throws rather than
                // handing back a null a later line would have to guess about.
                RenderInvokerReturn(member,
                    "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
                    + $".GetRequiredService<{GetInvokerInterfaceType(member)}>(_services)"
                    + $".InvokeAsync({paramName}, ct).ConfigureAwait(false)");
            }
        },
        returnType,
        new List<MethodParameter>
        {
            new(member.FullTypeName, paramName),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        },
        AccessModifier.Public,
        new MethodModifiers { IsAsync = true });

        // The preloaded shape, for a caller that already holds the row.
        //
        // ⚠️ Generated on the implementation whether or not this boundary is internal-only: the type
        // implements the internal interface either way, and the internal interface is where the shape
        // is declared. The remote implementation never sees it, which is the point — a tracked entity
        // does not cross a process boundary.
        if (member.IsMutation && !member.MutationCreates && member.EntityFullTypeName is not null)
        {
            AppendLine();
            XmlInheritDoc();
            Method(methodName, () =>
            {
                if (entersInternalCall)
                    AppendLine("using var __scope = _callContext?.EnterInternalCall();");

                RenderInvokerReturn(member,
                    "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions"
                    + $".GetRequiredService<{GetInvokerInterfaceType(member)}>(_services)"
                    + $".InvokeAsync({paramName}, entity, ct).ConfigureAwait(false)");
            },
            returnType,
            new List<MethodParameter>
            {
                new(member.FullTypeName, paramName),
                new(member.EntityFullTypeName!, "entity"),
                new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
            },
            AccessModifier.Public,
            new MethodModifiers { IsAsync = true });
        }

        // Unwrapped overload — creates instance from properties and delegates
        if (member.HasInputProperties)
        {
            XmlInheritDoc();
            var unwrappedParams = BuildUnwrappedMethodParams(member);
            var varName = $"__{MemberParamName(member)}";

            Method(methodName, () =>
            {
                AppendLine($"var {varName} = new {member.FullTypeName}");
                AppendLine("{");
                IncreaseIndent();
                foreach (var prop in member.InputProperties)
                {
                    var propParamName = TemplateHelpers.ToCamelCase(prop.Name);
                    AppendLine($"{prop.Name} = {propParamName},");
                }
                DecreaseIndent();
                AppendLine("};");
                AppendLine($"return {methodName}({varName}, ct);");
            }, returnType, unwrappedParams, AccessModifier.Public);
        }
    }

    private List<MethodParameter> BuildUnwrappedMethodParams(BoundaryMemberModel member)
    {
        var ordered = OrderProperties(member.InputProperties.AsImmutableArray());
        var list = ordered
            .Select(p =>
            {
                var typeName = GetParamTypeName(p);
                var defaultValue = GetDefaultValueForParam(p);
                return new MethodParameter(typeName, TemplateHelpers.ToCamelCase(p.Name))
                {
                    DefaultValue = defaultValue
                };
            })
            .ToList();
        list.Add(new MethodParameter("global::System.Threading.CancellationToken", "ct")
        {
            DefaultValue = "default"
        });
        return list;
    }

    // =========================================================================
    // DI Extension
    // =========================================================================

    private void RenderDiExtension()
    {
        var className = $"{_boundary.TypeName}Extensions";

        XmlSummary($"DI registration for <see cref=\"{_boundary.InterfaceName}\"/>.");

        Class(className, RenderDiExtensionBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    private void RenderDiExtensionBody()
    {
        var methodName = $"Add{_boundary.TypeName}";

        XmlSummary($"Registers <see cref=\"{_boundary.InterfaceName}\"/> with the specified invocation mode.");
        XmlParam("services", "The service collection.");
        XmlParam("mode", "The boundary invocation mode. Defaults to Local.");
        XmlReturns("The service collection for chaining.");

        Method(methodName, RenderDiSwitchBody,
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            new List<MethodParameter>
            {
                new("global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")
                {
                    IsExtension = true
                },
                new("global::Pragmatic.Actions.Boundary.BoundaryMode", "mode")
                {
                    DefaultValue = "global::Pragmatic.Actions.Boundary.BoundaryMode.Local"
                }
            },
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });

        // AddLocal and AddRemote are generated in separate partial files (Local.g.cs, Remote.g.cs)
    }

    private void RenderDiSwitchBody()
    {
        AppendLine("return mode switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("global::Pragmatic.Actions.Boundary.BoundaryMode.Local => AddLocal(services),");
        if (_hasRemote)
            AppendLine("global::Pragmatic.Actions.Boundary.BoundaryMode.Remote => AddRemote(services),");
        AppendLine(
            $"_ => throw new global::System.NotSupportedException($\"BoundaryMode '{{mode}}' is not yet supported for {_boundary.TypeName}.\")");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderAddLocalMethod()
    {
        Method("AddLocal", () =>
        {
            // Register sub-boundary implementations first (root depends on them)
            if (_hasSubBoundaries)
            {
                foreach (var sub in _boundary.SubBoundaries)
                {
                    AppendLine($"services.AddScoped<{sub.ImplementationName}>();");
                    if (!_boundary.IsInternal)
                    {
                        var subGuarded = GuardedImplementationName(sub.ImplementationName);
                        AppendLine($"services.AddScoped<{subGuarded}>();");
                        AppendLine($"services.AddScoped<{sub.InterfaceName}>(sp => sp.GetRequiredService<{subGuarded}>());");
                    }
                    else
                    {
                        AppendLine($"services.AddScoped<{sub.InterfaceName}>(sp => sp.GetRequiredService<{sub.ImplementationName}>());");
                    }

                    // The group's internal twin, resolving the UNGUARDED implementation — the same
                    // object the root's internal interface hands out through .{Group}, because both
                    // resolve one scoped concrete type.
                    //
                    // ⚠️ Read the resolved type, not the line: the guarded twin also implements the
                    // group's public interface, so naming it here would make the injected twin and
                    // Root.{Group} two objects with two permission behaviours under one interface.
                    // Spec 7.25 draws the distinction on a group by *instance*, and that is what keeps
                    // holding — the public interface above still resolves to the guarded one.
                    //
                    // Registering it opens no unguarded second door: the twin is `internal`, so its
                    // only possible caller already injects the unguarded root and is standing in the
                    // same room. Left unregistered, a [MessageHandler] or a [Job] following the
                    // generated documentation to this interface cannot be constructed at all, and the
                    // message is nacked and dropped with no row and no error.
                    if (sub.HasInternalTwin)
                    {
                        AppendLine(
                            $"services.AddScoped<{sub.InternalInterfaceName}>(sp => sp.GetRequiredService<{sub.ImplementationName}>());");
                    }
                }

                AppendLine();
            }

            // Register root implementation
            var implType = _boundary.ImplementationName;
            AppendLine($"services.AddScoped<{implType}>();");

            if (!_boundary.IsInternal)
            {
                // The public interface resolves to the guarded twin: a caller outside this module gets
                // the implementation that lets the invoked operation's permission be asked.
                var guardedType = GuardedImplementationName(implType);
                AppendLine($"services.AddScoped<{guardedType}>();");
                AppendLine(
                    $"services.AddScoped<{_boundary.InterfaceName}>(sp => sp.GetRequiredService<{guardedType}>());");
            }

            // Internal interface (always registered)
            AppendLine(
                $"services.AddScoped<{_boundary.InternalInterfaceName}>(sp => sp.GetRequiredService<{implType}>());");

            // Package action invokers — fused from [UsePackage<T>] on this module
            if (!_packageRegistrations.IsEmpty)
            {
                RenderPackageBoundaryBridge();

                AppendLine();
                Comment("Package action invokers (fused via [UsePackage])");
                foreach (var reg in _packageRegistrations)
                {
                    if (reg.IsMutation)
                    {
                        AppendLine($"services.AddScoped<global::Pragmatic.Actions.Invoker.IMutationInvoker<{reg.ActionType}, {reg.EntityType}>, {reg.InvokerType}>();");
                    }
                    else if (reg.IsVoid)
                    {
                        AppendLine($"services.AddScoped<global::Pragmatic.Actions.Invoker.IVoidDomainActionInvoker<{reg.ActionType}>, {reg.InvokerType}>();");
                    }
                    else
                    {
                        AppendLine($"services.AddScoped<global::Pragmatic.Actions.Invoker.IDomainActionInvoker<{reg.ActionType}, {reg.ReturnType}>, {reg.InvokerType}>();");
                    }
                }
            }

            AppendLine("return services;");
        },
        "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
        new List<MethodParameter>
        {
            new("global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")
        },
        AccessModifier.Private,
        new MethodModifiers { IsStatic = true });
    }

    /// <summary>What the operation is called in the generated signature.</summary>
    private static string MemberParamName(BoundaryMemberModel member)
        => member switch
        {
            { IsQuery: true } => "query",
            { IsMutation: true } => "mutation",
            _ => "action"
        };

    /// <summary>
    ///     Answers, from this module's boundary, the unkeyed <c>DbContext</c>/<c>IUnitOfWork</c> an
    ///     imported package asks for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The package's invoker was generated where no boundary existed, so its constructor asks
    ///         for those services with no key and cannot be changed by whoever imports it. A key
    ///         therefore cannot be pushed down; the request has to be answered where it lands, which is
    ///         this container.
    ///     </para>
    ///     <para>
    ///         Only the services an imported operation actually named are bridged, and only when the
    ///         import named a boundary — a package that touches no persistence produces nothing here,
    ///         and an import that named no boundary is <c>PRAG0449</c> rather than a silent
    ///         registration.
    ///     </para>
    /// </remarks>
    private const string PackageDbContext = "global::Microsoft.EntityFrameworkCore.DbContext";

    private const string PackageUnitOfWork = "global::Pragmatic.Persistence.Repository.IUnitOfWork";

    private void RenderPackageBoundaryBridge()
    {
        if (string.IsNullOrEmpty(_boundary.PackageBoundaryKey))
            return;

        var declared = _packageRegistrations
            .SelectMany(r => r.BoundaryKeyedServices.AsImmutableArray())
            .ToList();

        if (declared.Count == 0)
            return;

        // ⚠️ Both, whichever one the package's operations named. A package that owns entities also
        // brings their generated repositories, and those ask for the context and its unit of work
        // together — so bridging only what an action declared left the container unable to construct a
        // repository nobody wrote, which is where the two halves of an import came apart.
        var needed = declared
            .Concat([PackageDbContext, PackageUnitOfWork])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        AppendLine();
        Comment($"Imported package operations resolve these from {_boundary.PackageBoundaryKey}");
        foreach (var service in needed)
        {
            AppendLine($"services.AddScoped<{service}>(sp => "
                       + "global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions"
                       + $".GetRequiredKeyedService<{service}>(sp, typeof({_boundary.PackageBoundaryKey})));");
        }
    }
}
