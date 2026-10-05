using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Redaction.Models;
using Pragmatic.SourceGenerator.Features.Redaction.Templates;
using Pragmatic.SourceGenerator.Features.Redaction.Transforms;

namespace Pragmatic.SourceGenerator.Features.Redaction;

/// <summary>
///     Emits the assembly's <c>IRedactionMap</c> from the members marked <c>[NotLogged]</c> and
///     <c>[PersonalData]</c>, and registers it.
/// </summary>
/// <remarks>
///     <para>
///         Driven by the attributes, not by the kind of type carrying them. A map emitted for message
///         types only would let a mutation input with a marked member go out whole, and enumerating
///         kinds (message, then action, then entity) only moves that gap. Anything with a marked
///         property is covered, including a type nobody anticipated.
///     </para>
///     <para>
///         Consumed by <c>Pragmatic.Redaction.DeclaredRedactor</c> at the logging boundary.
///     </para>
/// </remarks>
internal static class RedactionFeature
{
    private const string NotLoggedAttribute = "Pragmatic.NotLoggedAttribute";
    private const string PersonalDataAttribute = "Pragmatic.Privacy.PersonalDataAttribute";

    /// <returns>
    ///     The metadata entry that tells the Composition host to call this assembly's registration.
    /// </returns>
    /// <remarks>
    ///     Emitting the map and the registration method was never enough: the host discovers what to
    ///     call from these entries, and redaction had none. Measured on a real consumer —
    ///     <c>AddGeneratedRedactionMap</c> appeared in four projects and was called by nobody, while
    ///     <c>AddGeneratedJsonContext</c>, which does emit an entry, was called for each module. The
    ///     attributes were inert wherever they were used.
    /// </remarks>
    /// <param name="context">The generator initialization context.</param>
    /// <param name="jsonContextEmitted">
    ///     Whether this compilation emits the generated JSON context, which then covers the redacted
    ///     types (<see cref="JsonRoots" />). The map hands it to the redactor as the metadata to
    ///     serialize them with; without it the redactor has only reflection, which Native AOT refuses.
    /// </param>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<bool> jsonContextEmitted)
    {
        var notLogged = Collect(context, NotLoggedAttribute, RedactedMemberTransform.NotLogged);
        var personalData = Collect(context, PersonalDataAttribute, RedactedMemberTransform.PersonalData);

        // ForAttributeWithMetadataName does not report [property: NotLogged] on a positional record
        // parameter — measured, not assumed: the same source fails through FAWMN and succeeds through
        // a symbol scan. That is why the implementation this replaces scanned symbols, a fact its
        // tests stated and I had to rediscover. Records therefore get their own pass, over the type,
        // and the union is de-duplicated downstream.
        var positional = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax
                                             { ParameterList.Parameters.Count: > 0 },
                RedactedMemberTransform.FromRecordDeclaration)
            .SelectMany(static (m, _) => m)
            .Collect();

        // The assembly must be able to compile what we emit. Pragmatic.Mapping, for one, references
        // neither Abstractions nor the DI extensions, and emitting there produces CS0234 rather than
        // a map. This is the feature's activation condition, and the emit-even-when-empty rule
        // applies INSIDE it: within an assembly that could have declared something, an
        // absent map means the generator did not run and nothing else.
        var target = context.CompilationProvider.Select(static (c, _) => new EmitTarget(
            c.AssemblyName ?? "",
            c.GetTypeByMetadataName("Pragmatic.Serialization.IRedactionMap") is not null
            && c.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions") is not null));

        // What a type reaches below its own face: the classified members of an owned record, a nested
        // value object or a collection of children, as paths. Driven by the type rather than by the
        // attribute, because the attribute is on the nested type and the map is keyed by the outer one
        // — and the nested type is routinely in a referenced assembly.
        var paths = context.SyntaxProvider
            .CreateSyntaxProvider(
                DeclaredRedactionPathTransform.CouldHoldAClassifiedType,
                DeclaredRedactionPathTransform.Transform)
            .Where(static found => found.Count > 0)
            .SelectMany(static (found, _) => found.AsImmutableArray())
            .Collect();

        var all = notLogged.Combine(personalData).Combine(positional).Combine(paths).Combine(target);

        context.RegisterSourceOutputSafe(all.Combine(jsonContextEmitted), static (ctx, input) =>
        {
            var (data, contextEmitted) = input;
            var ((((fromNotLogged, fromPersonalData), fromRecords), fromPaths), target) = data;

            if (!target.CanEmit)
                return;

            var assembly = target.AssemblyName;
            var members = fromNotLogged.AddRange(fromPersonalData).AddRange(fromRecords).AddRange(fromPaths);

            var template = new RedactionMapTemplate(members, assembly, contextEmitted);
            ctx.AddSource(template.RenderOutput());

            var registration = new RedactionRegistrationTemplate(template.Namespace, assembly);
            ctx.AddSource(registration.RenderOutput());

            // The assembly attribute a referenced module publishes. The MetadataEntry returned below
            // only covers the host's own compilation; without this one a module's map is emitted,
            // registered by a method nobody calls, and the attributes stay inert.
            var metadata = new RedactionMetadataTemplate(template.Namespace, assembly);
            ctx.AddSource(metadata.RenderOutput());
        });

        // Under the same condition that emits the registration, tell the host to call it.
        return all.Select(static (data, _) =>
        {
            var ((((_, _), _), _), target) = data;
            if (!target.CanEmit)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            var mapNamespace = RedactionMapTemplate.NamespaceFor(target.AssemblyName);

            return new EquatableArray<Composition.Models.MetadataEntry>(
                ImmutableArray.Create(
                    Composition.Models.HostLocalRegistration.Create(
                        Composition.MetadataCategoryIds.Redaction,
                        "1.0",
                        Core.GeneratedRegistrationNames.RedactionFqn(mapNamespace, target.AssemblyName))));
        });
    }

    /// <summary>
    ///     The JSON shape of every type the map carries, as roots of the generated JSON context.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="Register" /> because the two run in opposite directions: these roots
    ///     go into the JSON context, and whether that context is emitted comes back into the map.
    /// </remarks>
    public static IncrementalValueProvider<ImmutableArray<Serialization.Models.JsonRootContribution>> JsonRoots(
        IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider
            .CreateSyntaxProvider(
                DeclaredRedactionPathTransform.CouldHoldAClassifiedType,
                RedactedTypeJsonTransform.Transform)
            .Where(static root => root is not null)
            .Select(static (root, _) => root!)
            .Collect();

    /// <summary>Whether this compilation can see the types the generated map needs, and its name.</summary>
    private readonly record struct EmitTarget(string AssemblyName, bool CanEmit);

    private static IncrementalValueProvider<ImmutableArray<RedactedMemberModel>> Collect(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn,
        System.Func<GeneratorAttributeSyntaxContext, System.Threading.CancellationToken, RedactedMemberModel?> transform)
        => context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                // Deliberately permissive. The attribute reaches a property through more than one
                // syntax — a property declaration, and a positional record parameter carrying
                // [property: ...] — and filtering on the node shape silently dropped the second.
                // The transform decides, on TargetSymbol being an IPropertySymbol, which is the
                // question that actually matters; the predicate is a performance filter, and
                // ForAttributeWithMetadataName has already narrowed this to the attribute.
                static (_, _) => true,
                transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!.Value)
            .Collect();
}
