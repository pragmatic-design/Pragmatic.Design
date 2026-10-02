namespace Pragmatic.Authoring;

/// <summary>
///     Links a member (mutation, action, domain method, handler, job) to a use-case identifier,
///     for permanent traceability between code and requirement. Inert at runtime — read at compile
///     time by the generated use-case catalog,
///     <c>{Assembly}.Generated.PragmaticUseCases</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Until 2026-09 this summary said "consumed by tooling, analyzers, and the generated
///         use-case catalog" and none of the three existed: nothing in the framework read this
///         attribute or <see cref="RuleAttribute" />. It read as working because the other half of
///         the sentence — "inert at runtime" — is true, so nothing happening looked like the
///         documented behaviour. The catalog was built rather than the pair deleted; what
///         is named here now is a single generated class, and
///         <see cref="UseCaseDescriptor" /> is what it holds.
///     </para>
///     Stacks with <see cref="RuleAttribute"/> (business rules in natural language) and
///     <c>Raises&lt;TEvent&gt;</c> (declared domain events) on the same declaration:
///     <code>
///     [Mutation(Mode = MutationMode.Create)]
///     [UseCase("DRG-CREATE", Title = "Add a drug to the catalogue")]
///     [Rule("Drug code is unique within the inventory")]
///     public partial class CreateDrugMutation : Mutation&lt;Drug&gt; { }
///     </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class UseCaseAttribute(string id) : Attribute
{
    /// <summary>The use-case identifier (e.g. <c>"DRG-DISPENSE"</c>).</summary>
    public string Id { get; } = id;

    /// <summary>Optional human-readable title of the use case.</summary>
    public string? Title { get; init; }
}
