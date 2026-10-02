using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Privacy.Diagnostics;

/// <summary>
/// Diagnostic descriptors for the Privacy feature. Range PRAG2900–PRAG2999.
/// </summary>
/// <remarks>
/// None of these fire until the compilation contains a <c>[DataSubject]</c>. That is what keeps them
/// usable: warning about unclassified strings across a solution that has not opted into the analysis
/// would be noise nobody can act on, and noise is how a real finding gets ignored. Once a subject
/// exists, the scope is exactly the entities reachable from it.
/// </remarks>
internal static class PrivacyDiagnostics
{
    /// <summary>PRAG2900: personal data with no path to a subject can never be erased.</summary>
    public static readonly DiagnosticDescriptor NoPathToSubject = DiagnosticFactory.Error(
        "PRAG2900",
        "Personal data cannot be reached from any data subject",
        "Type '{0}' declares personal data but no path to a [DataSubject] — its rows can never be erased.",
        "Add [LinksToSubject(nameof(...))] naming the property that leads to the subject. " +
        "Without it an erasure request silently leaves this data behind, which is the failure mode that " +
        "surfaces during an inspection rather than during a build.");

    /// <summary>PRAG2901: Retain without a reason.</summary>
    public static readonly DiagnosticDescriptor RetainWithoutReason = DiagnosticFactory.Error(
        "PRAG2901",
        "Retained personal data needs a stated reason",
        "Property '{0}.{1}' uses ErasureStrategy.Retain without a Reason.",
        "Set Reason to the obligation that justifies keeping the value. Retaining data against an " +
        "erasure request is legitimate; retaining it without saying why is the same as forgetting to " +
        "erase it, and the register cannot tell the two apart.");

    /// <summary>PRAG2902: DestroyKey on a property that is not encrypted.</summary>
    public static readonly DiagnosticDescriptor DestroyKeyWithoutEncryption = DiagnosticFactory.Error(
        "PRAG2902",
        "DestroyKey requires the property to be encrypted",
        "Property '{0}.{1}' uses ErasureStrategy.DestroyKey but is not marked Encrypted.",
        "Set Encrypted = true, or choose another strategy. Destroying a key that protects nothing " +
        "reports an erasure that did not happen.");

    /// <summary>PRAG2909: Null on a property whose type cannot hold null.</summary>
    public static readonly DiagnosticDescriptor NullOnNonNullableProperty = DiagnosticFactory.Error(
        "PRAG2909",
        "Null erasure needs a property that can hold null",
        "Property '{0}.{1}' uses ErasureStrategy.Null but its type '{2}' cannot hold null.",
        "Make the property nullable, or choose Anonymize, Pseudonymize or Retain. The plan declares " +
        "the field will be cleared and the write fails at SaveChanges against the NOT NULL column, so " +
        "the erasure does not happen and the subject's request fails — at the moment it is exercised, " +
        "which is the worst moment to discover it.");

    /// <summary>PRAG2903: an unclassified string on an entity reachable from a subject.</summary>
    public static readonly DiagnosticDescriptor UnclassifiedProperty = DiagnosticFactory.Error(
        "PRAG2903",
        "Property on a subject-reachable entity is not classified",
        "Property '{0}.{1}' is reachable from a [DataSubject] but carries no [PersonalData] classification.",
        "Classify it, or mark it as non-personal. The point is that the decision is recorded: an " +
        "unclassified field is indistinguishable from one nobody thought about.");

    /// <summary>PRAG2904: special-category data exposed without an authorization policy.</summary>
    /// <remarks>
    ///     Reported against the endpoint rather than the property: the property is correctly declared,
    ///     and the change that resolves this is on the endpoint.
    /// </remarks>
    public static readonly DiagnosticDescriptor SpecialCategoryWithoutPolicy = DiagnosticFactory.Warning(
        "PRAG2904",
        "Special-category data exposed without an authorization policy",
        "Endpoint '{2}' exposes '{0}.{1}', which is DataCategory.Special, without naming who may read it.",
        "Add a permission, role or policy to the endpoint. Requiring only a signed-in user lets every " +
        "account in the system read it, and special categories are where an unintended disclosure is " +
        "hardest to remedy.");

    // PRAG2905 ("personal data over an unencrypted transport") was declared here and removed unused.
    // Whether a transport is encrypted is a property of the deployment — a connection string, a broker's
    // configuration, a TLS terminator in front of it — and none of that is visible to a compiler. The
    // descriptor could only ever have fired on a guess, and a security warning that guesses trains
    // people to ignore security warnings. The range stays free for a diagnostic that can be decided.

    /// <summary>PRAG2906: [LinksToSubject] naming a property that does not exist or cannot be navigated.</summary>
    public static readonly DiagnosticDescriptor InvalidSubjectPath = DiagnosticFactory.Error(
        "PRAG2906",
        "[LinksToSubject] names a property that cannot be followed",
        "Type '{0}' declares [LinksToSubject(\"{1}\")] but that property does not exist or is not a navigation.",
        "Name a navigation or foreign-key property that leads towards the subject. A path that cannot " +
        "be followed produces an erasure that misses rows without failing.");

    /// <summary>PRAG2907: a field the erasure plan has no way to write.</summary>
    /// <remarks>
    ///     The plan writes either the property directly, when its setter is public, or the internal
    ///     <c>Set{Property}</c> that Pragmatic.Persistence generates for an entity. A type that is neither
    ///     leaves the field unreachable, and an erasure strategy that cannot be carried out is worse than
    ///     no strategy at all: the register says the field is cleared and nothing ever clears it.
    /// </remarks>
    public static readonly DiagnosticDescriptor UnwritableProperty = DiagnosticFactory.Error(
        "PRAG2907",
        "Personal data with an erasure strategy that cannot be applied",
        "Property '{0}.{1}' is erased by '{2}' but nothing can write it: its setter is not public and " +
        "'{0}' is not an [Entity], so no Set{1} is generated.",
        "Make the setter public, or declare the type an entity so the framework generates its setters. " +
        "Until then the plan leaves the field untouched, and the erasure it declares does not happen.");

    /// <summary>PRAG2908: a subject identified by a type the generated adapters cannot look up.</summary>
    /// <remarks>
    ///     <para>
    ///         The registry hands back an identity as a <c>string</c> — it stores identities encrypted and
    ///         has nothing else to hand back. Turning that string into a filter needs a conversion the
    ///         database can be asked about, and only a handful of types have one.
    ///     </para>
    ///     <para>
    ///         Reported rather than skipped. Without the adapters the subject's rows are invisible to an
    ///         access request and untouched by an erasure, and the register would state a capability the
    ///         application does not have.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor UnsupportedSubjectIdentifierType = DiagnosticFactory.Warning(
        "PRAG2908",
        "Subject identifier type cannot be matched against a subject reference",
        "'{0}.{1}' identifies the subject but is of type '{2}', which the generated data source and " +
        "erasure step cannot compare against the identity the registry resolves.",
        "Use string, System.Guid, int or long for the identifier property, or supply " +
        "IPersonalDataSource and IErasureStep implementations by hand for this subject. Nothing is " +
        "generated for it in the meantime.");

    /// <summary>PRAG2910: <c>[RecordAccess]</c> where there is no audit trail to record into.</summary>
    /// <remarks>
    ///     <para>
    ///         The attribute is the one way an operation says its reads must leave a trace, and it is
    ///         used precisely where the answer to "who looked at this" has to exist later. Silently
    ///         emitting nothing would leave the author believing the record is being written, and the
    ///         gap would be found by the question it was meant to answer.
    ///     </para>
    ///     <para>
    ///         A warning, not an error: the fix is a package reference, and failing the build of a module
    ///         that has merely declared an intention would be out of proportion. The register also reports
    ///         the operation as unrecorded, so the two agree.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor RecordAccessWithoutAuditTrail = DiagnosticFactory.Warning(
        "PRAG2910",
        "[RecordAccess] has no audit trail to write to",
        "'{0}' declares [RecordAccess] but this assembly does not reference Pragmatic.Audit, so its " +
        "reads are not recorded.",
        "Add a reference to Pragmatic.Audit, or remove the attribute. Leaving it produces an operation " +
        "the Article 30 register lists as unrecorded while its declaration says otherwise.");

    /// <summary>
    ///     PRAG2911: an operation composes another and declares no personal data, so the register does
    ///     not list it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The register reads an action's reach off the types of its dependencies — an
    ///         <c>IRepository&lt;TEntity&gt;</c> or an <c>IMutationInvoker&lt;TMutation, TEntity&gt;</c>
    ///         names the entity. A boundary interface names none, so an operation composing through one
    ///         reaches whatever those operations reach and the register cannot tell. Without this it
    ///         simply left the register, which reads as "processes nothing".
    ///     </para>
    ///     <para>
    ///         ⚠️ Recognised by the dependency <b>not resolving</b>, not by its name. The interface is
    ///         written by this generator in this same pass, so the module's compilation does not contain
    ///         it; in a build that otherwise succeeds an unresolved dependency type can only be
    ///         generated code, and anything else is a CS0246 the author is already looking at.
    ///     </para>
    ///     <para>
    ///         A warning, and answerable two ways: <c>[ProcessesData&lt;TEntity&gt;]</c> for what it
    ///         reaches, or the non-generic <c>[ProcessesData]</c> for "reviewed, reaches none". Without
    ///         the second the only way to silence it would be to name an entity the operation does not
    ///         touch, which puts a lie in the register to quieten a warning.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor CompositionWithoutDeclaredData = DiagnosticFactory.Warning(
        "PRAG2911",
        "Composing operation declares no personal data",
        "'{0}' composes through '{1}', whose entities cannot be inferred, and declares no " +
        "[ProcessesData] — so the Article 30 register does not list it as processing anything.",
        "Add [ProcessesData<TEntity>] for each entity it reaches through that operation, or the " +
        "non-generic [ProcessesData] to state that it reaches none.");

    /// <summary>
    ///     PRAG2912: <c>Anonymize</c> on a non-nullable reference type that has no anonymous value.
    /// </summary>
    /// <remarks>
    ///     A string is anonymised to the empty string, a value type to its default, a nullable property
    ///     to null. Anything else — a <c>byte[]</c>, an owned object — has no value both anonymous and
    ///     generic: null fails at SaveChanges against the NOT NULL column, and any value the
    ///     generator made up would be something nobody decided to store.
    /// </remarks>
    public static readonly DiagnosticDescriptor NoAnonymousValue = DiagnosticFactory.Error(
        "PRAG2912",
        "Anonymize needs a type with an anonymous value",
        "Property '{0}.{1}' uses ErasureStrategy.Anonymize, but its type '{2}' has no value that identifies " +
        "nobody and that a NOT NULL column accepts.",
        "Make the property nullable, or choose Pseudonymize, Delete or Retain. The plan leaves the field " +
        "alone rather than write null into it, so without a change the erasure would not reach it.");

    /// <summary>
    ///     PRAG2913: a <c>[ProcessesData&lt;TEntity&gt;]</c> naming an entity the generator already infers.
    /// </summary>
    /// <remarks>
    ///     Info, not a warning: the register is right either way. But two sources for one fact drift — the
    ///     load goes, the declaration stays, and the register goes on listing an entity nothing reaches — so
    ///     the declaration is for what the generator cannot see: a service, a raw command, a boundary
    ///     interface. Not reported on an operation that composes through a dependency this
    ///     compilation cannot resolve: there a declaration may be answering for the composition, which is
    ///     what PRAG2911 asks for.
    /// </remarks>
    public static readonly DiagnosticDescriptor RedundantProcessesData = DiagnosticFactory.Info(
        "PRAG2913",
        "[ProcessesData] restates what the generator infers",
        "[ProcessesData<{1}>] on '{0}' restates what the generator infers from its dependencies and loads — " +
        "remove it; the register lists '{1}' without it.",
        "An operation's IRepository/IReadRepository/IMutationInvoker dependencies and its [LoadEntity], " +
        "[LoadEntities], [LoadFrom] and [LoadCurrentUser] are read by the generator. [ProcessesData<T>] is " +
        "for what it cannot see.");
}
