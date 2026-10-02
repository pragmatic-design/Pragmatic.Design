using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     Diagnostic descriptors for Pragmatic.Persistence.EFCore source generator.
///     Range: PRAG0600-PRAG0699
/// </summary>
internal static class PersistenceDiagnostics
{
    // =========================================================================
    // Errors (PRAG0600-0649)
    // =========================================================================

    // PRAG0600 (TypeMustBePartial) and PRAG0602 (DatabaseMustBePartial) belong to the companion
    // analyzer (Pragmatic.SourceGenerator.Analyzers, NotPartialDiagnosticDescriptors), which is their
    // only source: it reports them on the type declaration so the "Make class partial" code fix can act.
    // Do not declare them here too — one ID, one owner.

    // NOTE: PRAG0601 and PRAG0603/0604 are deliberately unassigned — a descriptor nothing reports would
    // make the codebase claim a diagnostic it does not emit. The cross-database [ReadAccess] hazard is
    // reported by PRAG0706 (ReadAccessDiagnostics), where the topology is known.

    // =========================================================================
    // Warnings (PRAG0650-0699)
    // =========================================================================

    // ⚠️ PRAG0650 — "navigation without foreign key" — is left unused rather than recycled: a reader
    // who meets it in an old build log should find nothing here rather than something else. The
    // condition it named cannot occur. A relation is declared, so the generator writes the key itself
    // and it is always among the entity's properties; a navigation written by hand is PRAG0619, an
    // error — with no key at all, or with a key under another name, it reports PRAG0619 and PRAG0651.
    // No [Obsolete] before 1.0: a thing is removed, not deprecated.

    public static readonly DiagnosticDescriptor PropertyTypeMayNeedConverter = DiagnosticFactory.Warning(
        "PRAG0651",
        "Property may need value converter",
        "Property '{0}' on '{1}' has type '{2}' which may need a value converter for EF Core",
        "Register a ValueConverter in EntityConfiguration or use a supported primitive type.");

    /// <summary>
    ///     PRAG0652: a <c>ProtectedValue</c> property, and the package that stores one is not
    ///     referenced.
    /// </summary>
    /// <remarks>
    ///     ⚠️ An error, and not a warning, because there is nothing the author can do afterwards: the
    ///     generated configuration names <c>ProtectedValueConverter</c>, and without the package that
    ///     is a CS0234 inside generated source — a message about a file nobody wrote, pointing away
    ///     from the declaration that caused it. Said here, it names the property and the package.
    ///     There is no fallback to warn about either: without the converter the property has no
    ///     mapping at all, and <c>ErasureStrategy.DestroyKey</c> goes with it.
    /// </remarks>
    public static readonly DiagnosticDescriptor ProtectedValueNeedsCryptographyEFCore = DiagnosticFactory.Error(
        "PRAG0652",
        "A protected value needs Pragmatic.Cryptography.EFCore",
        "Property '{0}' on '{1}' is a ProtectedValue, which is stored through ProtectedValueConverter — "
        + "add a reference to Pragmatic.Cryptography.EFCore",
        "Reference Pragmatic.Cryptography.EFCore, or store something other than a ProtectedValue.");

    // =========================================================================
    // Relation Attribute Diagnostics (PRAG0610-0619)
    // =========================================================================

    // The whole block is live: RelationValidator reports these from the entity transform and
    // EntityCoreFeature materializes them. Each one flags generated code that cannot work —
    // EntityConfigurationTemplate emits `.WithOne(e => e.{Inverse})` / `.WithMany(e => e.{Inverse})`,
    // so an inverse that does not resolve becomes a CS1061 inside generated source — or a written
    // value that reaches nothing, which is worse, because nothing turns red.
    //
    // Two ideas are rejected and NOT reported by any of them, so that a reader does not look for them:
    //  • "declare OneToMany on the parent instead" — a style preference, and the one-directional
    //    ManyToOne is valid modelling: a parent frequently must NOT expose a collection
    //    (Showcase: Reservation → Property / RoomType);
    //  • a relation crossing a boundary — legal and used on purpose (Showcase: Invoice → Reservation),
    //    and already degraded to an FK-only relation. Only a relation crossing a DATABASE is dangerous,
    //    and that check belongs with the DbContext/[ReadAccess] rules, where the topology is known.
    //
    // A third — "a navigation declared without [Relation.*] is legitimate, so flagging it is noise on
    // correct code" — does not hold. Relations are declared: PRAG0619, an Error defined below, is that
    // very signal.

    /// <summary>PRAG0610: the two ends give the child's navigation two different names.</summary>
    /// <remarks>
    ///     A relationship resolves once, and the child's navigation is the child's own member: it names
    ///     it, or the parent names it for it with <c>Inverse</c>. When both do and they disagree there
    ///     is no honest precedence — whichever loses is a written name that produces nothing, which is
    ///     the shape <c>PRAG0611</c> exists to keep out.
    /// </remarks>
    public static readonly DiagnosticDescriptor InverseContradictsTheDeclaredName = DiagnosticFactory.Error(
        "PRAG0610",
        "The two ends name the same navigation differently",
        "'{0}' names the navigation it generates on '{1}' \"{2}\", but '{1}' calls it \"{3}\" — one "
        + "member cannot have both names: drop the Inverse, or make the two agree",
        "The child's navigation is one member: naming it from both ends means one of the two names "
        + "is never used.");

    /// <summary>PRAG0611: the delete behaviour written on the side that does not own the relationship.</summary>
    /// <remarks>
    ///     <para>
    ///         A relationship has one delete behaviour, and it is read from the side that owns it —
    ///         the <c>OneToMany</c>, or the principal of a <c>OneToOne</c>. That is what makes the two
    ///         generated configurations agree instead of depending on the order the graph walked the
    ///         entities in.
    ///     </para>
    ///     <para>
    ///         The consequence is that a value written on the dependent side, where a counterpart
    ///         exists, is read by nobody. Without this diagnostic it would be a written instruction
    ///         that silently does nothing — so the rule and the reconciliation arrive together.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DeleteBehaviourNotOwned = DiagnosticFactory.Error(
        "PRAG0611",
        "The delete behaviour belongs on the side that owns the relationship",
        "'{0}' sets OnDelete on its relation to '{1}', but '{1}' declares the collection that owns "
        + "this relationship — move the value there, where it is read",
        "The owning side describes the relationship; a delete behaviour written on the dependent "
        + "side is never read.");

    public static readonly DiagnosticDescriptor AmbiguousRelation = DiagnosticFactory.Warning(
        "PRAG0612",
        "Ambiguous relation",
        "'{0}' declares {1} relations to '{2}' and this one leaves its navigation to the convention "
        + "(derived: '{3}') — with more than one relation to the same entity the name cannot come from "
        + "the target type: add [Relation.{4}<{2}>.WithNavigation(\"...\")]",
        "Several relations to the same entity all derive their navigation name from that entity's type "
        + "name, so they collide and the generator keeps only the first: name each one explicitly.");

    public static readonly DiagnosticDescriptor InversePropertyNotFound = DiagnosticFactory.Error(
        "PRAG0613",
        "Inverse navigation not found",
        "Inverse = \"{0}\" on the relation from '{1}' to '{2}' matches no navigation on '{2}' — name an "
        + "existing navigation of '{2}', or declare the other side on '{2}' "
        + "(e.g. [Relation.{3}<{1}>.WithNavigation(\"{0}\")])",
        "The generated EF Core configuration binds the inverse by member access "
        + "(.WithOne(e => e.Inverse) / .WithMany(e => e.Inverse)), so a name that does not exist on the "
        + "target entity becomes a compile error inside generated code.");

    public static readonly DiagnosticDescriptor InversePropertyTypeMismatch = DiagnosticFactory.Error(
        "PRAG0614",
        "Inverse navigation has the wrong type",
        "Inverse = \"{0}\" on the relation from '{1}' to '{2}' resolves to a navigation of type '{3}' — "
        + "point it at the navigation on '{2}' that is a '{1}' (or a collection of '{1}')",
        "The inverse navigation is the other end of the same relationship: it must point back to the "
        + "entity that declares it.");

    /// <summary>PRAG0618: a one-to-one where IsPrincipal does not say which end carries the key.</summary>
    /// <remarks>
    ///     <para>
    ///         Both ends of a one-to-one declare the same attribute, so nothing but <c>IsPrincipal</c>
    ///         distinguishes them. Exactly one end saying it is the whole answer.
    ///     </para>
    ///     <para>
    ///         With neither saying it, both ends behaved as the dependent and the relationship got
    ///         <b>two</b> foreign keys, one per table. With both saying it, neither carried one and the
    ///         relationship had <b>none</b>. A lone declaration that calls itself principal is the same
    ///         second case: there is no other declaration to hold the key.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor OneToOnePrincipalNotDecided = DiagnosticFactory.Error(
        "PRAG0618",
        "A one-to-one needs exactly one principal",
        "The one-to-one between '{0}' and '{1}' {2} — say which end is the principal with "
        + "[Relation.OneToOne<...>.WithNavigation(\"...\", IsPrincipal = true)] on that end, and only "
        + "on that end",
        "The principal is the end without the foreign key. Nothing but IsPrincipal can tell the two "
        + "ends of a one-to-one apart.");

    // =========================================================================
    // [PartOf] leans on a relation (PRAG0631-0634)
    // =========================================================================

    /// <summary>PRAG0631: <c>[PartOf&lt;T&gt;]</c> on an entity that declares no relation to <c>T</c>.</summary>
    /// <remarks>
    ///     The attribute says who writes the row, not how the row is linked — and a part is linked to
    ///     its whole. Without the relation nothing produces the column, the navigation or the cascade,
    ///     and the nested write lands on a row the database does not know belongs to anyone.
    /// </remarks>
    public static readonly DiagnosticDescriptor PartOfWithoutRelation = DiagnosticFactory.Error(
        "PRAG0631",
        "[PartOf] needs the relation it leans on",
        "'{0}' is [PartOf<{1}>] and declares no [Relation.ManyToOne<{1}>] — the ownership has no edge "
        + "to live on: declare the relation, on this entity",
        "[PartOf] is a role on a relation, not a relation of its own.");

    /// <summary>PRAG0632: several relations to the parent, and <c>Via</c> does not say which is the ownership.</summary>
    public static readonly DiagnosticDescriptor PartOfViaRequired = DiagnosticFactory.Error(
        "PRAG0632",
        "[PartOf] needs Via",
        "'{0}' declares more than one relation to '{1}' ({2}), so [PartOf<{1}>] has to say which one "
        + "carries the ownership: [PartOf<{1}>(Via = \"...\")]",
        "A part dies with its whole along one edge; with several edges to the same parent only a name "
        + "tells them apart.");

    /// <summary>PRAG0633: <c>Via</c> names no relation to the parent.</summary>
    public static readonly DiagnosticDescriptor PartOfViaNotFound = DiagnosticFactory.Error(
        "PRAG0633",
        "[PartOf] Via does not resolve",
        "'{0}' has no relation to '{1}' whose navigation is \"{2}\" — name one of: {3}",
        "Via is matched against the navigation names of the entity's [Relation.ManyToOne<TParent>] "
        + "declarations.");

    /// <summary>PRAG0634: the ownership edge does not cascade.</summary>
    /// <remarks>
    ///     A part has no life of its own, so it goes when its whole goes. The delete behaviour is read
    ///     from the side that owns the relationship — the parent's collection when it declares one —
    ///     so that is where the contradiction is written, and where the message points.
    /// </remarks>
    public static readonly DiagnosticDescriptor PartOfEdgeDoesNotCascade = DiagnosticFactory.Error(
        "PRAG0634",
        "A part dies with its whole",
        "'{0}' is [PartOf<{1}>] along \"{2}\", but that relation deletes with '{3}' — a part has no "
        + "life of its own, so the edge has to cascade: set OnDelete = DeleteBehavior.Cascade on the "
        + "side that owns it, or drop [PartOf]",
        "The relation that carries an ownership cascades; anything else leaves orphans the domain says "
        + "cannot exist.");

    // =========================================================================
    // Relations are declared, not written (PRAG0619, PRAG0635)
    // =========================================================================

    /// <summary>PRAG0619: a navigation or a foreign key written as a property.</summary>
    /// <remarks>
    ///     <para>
    ///         Both are members a <c>[Relation.*]</c> generates. Written by hand they describe the same
    ///         relationship in a second vocabulary that can be read at most halfway: a pair navigation +
    ///         key inferred from its shape gets every option at its default, and a lone key carries no
    ///         relationship at all — no constraint reaches the schema. The generator infers nothing
    ///         from the shape; it reports the member instead.
    ///     </para>
    ///     <para>
    ///         The attributes are the complete model, and everything downstream — schema, DTOs,
    ///         queries, the client — reads it from there.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor RelationWrittenByHand = DiagnosticFactory.Error(
        "PRAG0619",
        "A relation is declared, not written",
        "'{0}.{1}' is {2} — declare the relationship with [Relation.*] on '{0}' and remove the "
        + "property: the generator emits it, with its configuration",
        "Navigations and foreign keys are generated from [Relation.*]. A hand-written one is a second "
        + "model the generator does not read.");

    /// <summary>PRAG0635: an EF Core relational attribute on an entity property.</summary>
    /// <remarks>
    ///     <c>[ForeignKey]</c> and <c>[InverseProperty]</c> describe a relationship to EF Core, not to
    ///     Pragmatic. The generator never read them, so the database got the relationship while the
    ///     published model stayed silent about it.
    /// </remarks>
    public static readonly DiagnosticDescriptor EfCoreRelationAttribute = DiagnosticFactory.Error(
        "PRAG0635",
        "EF Core relational attributes do not declare a relation",
        "'{0}.{1}' carries [{2}], which describes a relationship to EF Core and not to Pragmatic — "
        + "declare it with [Relation.*] instead",
        "The generator does not read EF Core's relational attributes, so a relationship declared with "
        + "them exists in the database and nowhere else.");

    /// <summary>PRAG0616: a many-to-many with a join entity whose foreign keys nobody named.</summary>
    /// <remarks>
    ///     EF Core binds a skip navigation to shadow foreign keys it names after the navigation —
    ///     <c>RelatedToPersistenceId</c> — while the migration creates only the properties the join
    ///     entity declares. The table is written with one set of columns and read with another, and
    ///     the first write through the navigation dies on a column that does not exist. Nothing said
    ///     so until then, because a declaration nobody writes through never fails.
    /// </remarks>
    public static readonly DiagnosticDescriptor JoinEntityKeysNotNamed = DiagnosticFactory.Error(
        "PRAG0616",
        "Join entity foreign keys are not named",
        "The many-to-many from '{0}' to '{1}' uses the join entity '{2}' without naming its foreign "
        + "keys — add LeftKey and RightKey to the [Relation.ManyToMany<{1}, {2}>.WithNavigation(...)], "
        + "naming the properties of '{2}' that hold each side's key",
        "Without them EF Core binds shadow foreign keys named after the navigations, which the "
        + "migration never creates: the join table exists with the columns the entity declares and "
        + "the model looks for different ones.");

    /// <summary>PRAG0617: several relations to one type, and the inverse names would collide.</summary>
    /// <remarks>
    ///     <para>
    ///         <c>PRAG0612</c> guards the name on the side that declares, and says nothing once they are
    ///         all written. The <b>inverse</b> name has a default of its own — the declaring entity, in
    ///         the plural — so two relations to the same type produce the same member on the target and
    ///         the second is dropped by name, silently: the relation simply does not exist.
    ///     </para>
    ///     <para>
    ///         The self-referencing case is where it bites hardest, and it has already cost a running
    ///         host: keys named from the entity rather than the navigations produced the same column
    ///         twice and a primary key PostgreSQL refuses.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor InverseNameNotDeclared = DiagnosticFactory.Error(
        "PRAG0617",
        "The inverse navigation needs a name",
        "'{0}' declares {1} relations to '{2}' that each write a member on it, and this one leaves the "
        + "inverse to the convention (derived: '{3}') — name it with Inverse = \"...\", on every one of "
        + "them",
        "Relations to the same type derive the same inverse name, so they collide on the target and "
        + "only the first survives.");

    public static readonly DiagnosticDescriptor DuplicateNavigationName = DiagnosticFactory.Error(
        "PRAG0615",
        "Duplicate navigation name",
        "'{0}' already generates a navigation named '{1}' (to '{2}'); the relation to '{3}' would "
        + "generate a second one and is dropped — rename it with "
        + "[Relation.{4}<{3}>.WithNavigation(\"...\")]",
        "Two navigations cannot share a name on the same entity: the generator keeps the first and "
        + "silently discards the rest.");

    /// <summary>
    ///     Maps a validated <see cref="RelationDiagnosticKind" /> to the descriptor to report.
    /// </summary>
    public static DiagnosticDescriptor ForRelation(RelationDiagnosticKind kind) => kind switch
    {
        RelationDiagnosticKind.AmbiguousRelation => AmbiguousRelation,
        RelationDiagnosticKind.InverseNameNotDeclared => InverseNameNotDeclared,
        RelationDiagnosticKind.DeleteBehaviourNotOwned => DeleteBehaviourNotOwned,
        RelationDiagnosticKind.InverseContradictsTheDeclaredName => InverseContradictsTheDeclaredName,
        RelationDiagnosticKind.OneToOnePrincipalNotDecided => OneToOnePrincipalNotDecided,
        RelationDiagnosticKind.PartOfWithoutRelation => PartOfWithoutRelation,
        RelationDiagnosticKind.PartOfViaRequired => PartOfViaRequired,
        RelationDiagnosticKind.PartOfViaNotFound => PartOfViaNotFound,
        RelationDiagnosticKind.PartOfEdgeDoesNotCascade => PartOfEdgeDoesNotCascade,
        RelationDiagnosticKind.RelationWrittenByHand => RelationWrittenByHand,
        RelationDiagnosticKind.EfCoreRelationAttribute => EfCoreRelationAttribute,
        RelationDiagnosticKind.InversePropertyNotFound => InversePropertyNotFound,
        RelationDiagnosticKind.InversePropertyTypeMismatch => InversePropertyTypeMismatch,
        RelationDiagnosticKind.JoinEntityKeysNotNamed => JoinEntityKeysNotNamed,
        _ => DuplicateNavigationName
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // State Machine Validation (PRAG0620-PRAG0623, PRAG0638)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>PRAG0620: no enum value carries <c>[InitialState]</c>.</summary>
    /// <remarks>
    ///     Error, not a warning. The factory assigns the initial state, so with none declared a new
    ///     entity lands on the enum's numeric zero — which need not be a declared member at all, and
    ///     which no <c>[TransitionFrom]</c> names, so the machine refuses every transition out of it.
    ///     A row that cannot move is not something to warn about after the fact.
    /// </remarks>
    public static readonly DiagnosticDescriptor StateMachineMissingInitialState = DiagnosticFactory.Error(
        "PRAG0620",
        "State machine has no initial state",
        "State machine on '{0}' has no state marked with [InitialState] — the Create() factory has no default state to set",
        "Mark one enum value with [InitialState] to define the entry state for new entities.");

    /// <summary>PRAG0638: more than one enum value carries <c>[InitialState]</c>.</summary>
    /// <remarks>
    ///     The transform resolves the initial state with <c>FirstOrDefault</c>, so a second declaration
    ///     was discarded in silence and the winner was whichever value the enum happens to declare
    ///     first. Declaration order is not a decision the author made, and there is no reading of two
    ///     entry states that the generator could honour — hence Error rather than a warning that picks
    ///     one anyway.
    /// </remarks>
    public static readonly DiagnosticDescriptor StateMachineMultipleInitialStates = DiagnosticFactory.Error(
        "PRAG0638",
        "State machine has more than one initial state",
        "State machine on '{0}' marks {1} values with [InitialState] ({2}) — an entity has one entry state",
        "Leave [InitialState] on exactly one enum value.");

    public static readonly DiagnosticDescriptor StateMachineUnreachableState = DiagnosticFactory.Warning(
        "PRAG0621",
        "Unreachable state in state machine",
        "State '{0}' on '{1}' has no incoming transitions and is not marked [InitialState] — it can never be reached",
        "Add [TransitionFrom(...)] on other states to allow transitioning to this state, or mark it as [InitialState].");

    public static readonly DiagnosticDescriptor StateMachineInvalidTransitionSource = DiagnosticFactory.Error(
        "PRAG0622",
        "Invalid transition source in state machine",
        "[TransitionFrom({0})] on state '{1}' references a state that does not exist in enum '{2}'",
        "The source state in [TransitionFrom] must be a valid member of the enum.");

    /// <summary>PRAG0623: the state machine is declared against a property the entity does not have.</summary>
    /// <remarks>
    ///     The property name defaults to <c>Status</c>, which is right often enough that nobody writes it
    ///     — and wrong silently when the state is called something else. What arrived instead was four
    ///     <c>CS0103</c>s inside a generated file the author cannot edit, naming a property they never
    ///     wrote, with nothing to say that <c>Property</c> is the argument that fixes it.
    /// </remarks>
    public static readonly DiagnosticDescriptor StateMachinePropertyNotFound = DiagnosticFactory.Error(
        "PRAG0623",
        "State machine property does not exist on the entity",
        "[StateMachine<{0}>] on '{1}' governs a property named '{2}', which '{1}' does not have",
        "Name the property that holds the state: [StateMachine<TState>(Property = nameof(YourProperty))]. " +
        "It defaults to \"Status\".");

    // =========================================================================
    // Traits declared by hand (PRAG0624)
    // =========================================================================

    /// <summary>PRAG0624: a trait's properties are declared by hand, but only some of them.</summary>
    /// <remarks>
    ///     <para>
    ///         The generator stands down on a trait's properties when the entity declares them itself —
    ///         that is how an application takes over the shape of its own audit or soft-delete columns.
    ///         The decision is per trait, not per property: declare all of them or none.
    ///     </para>
    ///     <para>
    ///         Without this, declaring some produces no message of its own, only a <c>CS0102</c> for each
    ///         one the author wrote, blaming a duplicate against a generated file they never opened.
    ///         Nothing would say that adding the missing property is the fix, and the obvious reading —
    ///         delete the ones that "already exist" — is the opposite of it.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor PartialTraitProperties = DiagnosticFactory.Error(
        "PRAG0624",
        "A trait's properties are only partly declared",
        "'{0}' declares some of the properties for [{1}] but not all: {2} missing. Declare them too to "
        + "own the trait, or remove the ones you wrote and let the generator emit all of them",
        "The generator emits a trait's properties as a group, so it can only stand down on the group.");

    // =========================================================================
    // Uniqueness and self-assigned identity (PRAG0625-PRAG0627)
    // =========================================================================

    /// <summary>PRAG0625: the parts of one domain key ask for different uniqueness scopes.</summary>
    /// <remarks>
    ///     <para>
    ///         A composite <c>[LogicKey]</c> describes one index, and an index has one scope. With two
    ///         parts and two different answers, whichever one the generator picked would be wrong half
    ///         the time — and invisible either way, since both produce a working schema that means
    ///         something the author did not write.
    ///     </para>
    ///     <para>
    ///         The scope is a property of the key, so it only has to be said once: putting
    ///         <c>Scope = UniquenessScope.Global</c> on any one part and leaving the others alone is
    ///         the shape this catches, and it is almost always what happened.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MixedLogicKeyScope = DiagnosticFactory.Error(
        "PRAG0625",
        "The parts of a domain key disagree about its scope",
        "The [LogicKey] parts on '{0}' ask for different uniqueness scopes ({1}). A composite key is "
        + "one index and one index has one scope: say the same Scope on every part",
        "Uniqueness is a property of the key, not of each column in it.");

    /// <summary>PRAG0626: an entity carries an identifier it assigns itself, on a tenant-scoped table.</summary>
    /// <remarks>
    ///     <para>
    ///         <c>[Entity]</c> takes no identifier type, but an entity can still declare <c>PersistenceId</c> by hand, and doing so on an
    ///         <c>ITenantEntity</c> with a domain-assigned value puts two tenants one collision apart
    ///         on the primary key — where the only repair is a composite primary key, and therefore
    ///         two-column foreign keys through the whole schema.
    ///     </para>
    ///     <para>
    ///         A value carried over from a system being replaced is not identity: keep it as an
    ///         ordinary property with its own uniqueness, where it can be corrected and eventually
    ///         dropped without touching a single foreign key.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor TenantEntityWithManualId = DiagnosticFactory.Warning(
        "PRAG0626",
        "A tenant-scoped entity assigns its own primary key",
        "'{0}' is tenant-scoped and declares PersistenceId itself. Two tenants that assign the same "
        + "value collide on the primary key, which no tenant filter can separate",
        "Let the generator assign PersistenceId, and keep a carried-over identifier as an ordinary "
        + "property with [Unique] on it.");

    /// <summary>PRAG0627: a [Unique] index names a property the entity does not have.</summary>
    /// <remarks>
    ///     The names go in as strings, so <c>nameof</c> is a convention rather than a guarantee — and a
    ///     name that matches nothing produces a generated configuration the compiler rejects, blaming a
    ///     file the author never opened. This says which name was wrong and on which entity, which is
    ///     the part <c>CS1061</c> inside a <c>.g.cs</c> leaves out.
    /// </remarks>
    public static readonly DiagnosticDescriptor UniqueIndexPropertyNotFound = DiagnosticFactory.Error(
        "PRAG0627",
        "A unique index names a property that does not exist",
        "[Unique] on '{0}' names '{1}', which '{0}' does not have",
        "Use nameof so the compiler keeps the name honest when the property is renamed.");

    // =========================================================================
    // Which boundary owns an entity (PRAG0628-PRAG0630)
    // =========================================================================

    /// <summary>PRAG0628: more than one [Module] in one assembly.</summary>
    /// <remarks>
    ///     The host maps a database to a module and a module to its assembly, so two modules in one
    ///     assembly are two answers to "which database is this". Nothing chose between them: the second
    ///     overwrote the first in a dictionary, and which one that was is attribute order.
    /// </remarks>
    public static readonly DiagnosticDescriptor MultipleModulesInOneAssembly = DiagnosticFactory.Error(
        "PRAG0628",
        "An assembly declares more than one module",
        "'{0}' declares {1} [Module] classes — an assembly has one module, and every boundary in it "
        + "belongs to that module",
        "Split the second module into its own project, which is what the host addresses when it "
        + "assigns a database.");

    /// <summary>PRAG0629: an entity no boundary claims, where there is a choice to be made.</summary>
    /// <remarks>
    ///     With one boundary in the assembly there is nothing to declare — it owns everything. With two
    ///     or more, an unclaimed entity reaches no DbContext: no table, and the first write is the one
    ///     that says so.
    /// </remarks>
    public static readonly DiagnosticDescriptor EntityClaimedByNoBoundary = DiagnosticFactory.Error(
        "PRAG0629",
        "No boundary owns this entity",
        "'{0}' is owned by none of the {1} boundaries this assembly declares — add [Owns<{0}>] to the "
        + "one whose context it belongs in",
        "An entity outside every boundary has no DbContext, so no migration creates its table.");

    /// <summary>PRAG0630: two boundaries claiming the same entity.</summary>
    public static readonly DiagnosticDescriptor EntityClaimedByTwoBoundaries = DiagnosticFactory.Error(
        "PRAG0630",
        "Two boundaries own the same entity",
        "'{0}' is claimed by '{1}' and '{2}' — one entity belongs to one context, or the transaction "
        + "line runs through the middle of it",
        "Keep [Owns<T>] on the boundary that writes it; the other reads it with [ReadAccess<T>].");

    // =========================================================================
    // The domain key declared on the class (PRAG0636-PRAG0637)
    // =========================================================================

    /// <summary>PRAG0636: a class-level [LogicKey] names a part the entity does not have.</summary>
    /// <remarks>
    ///     The names go in as strings, because the form exists for keys the class does not declare —
    ///     a relation's generated foreign key has no symbol for <c>nameof</c>. So a typo is a real
    ///     possibility, and without this it produced a lookup and an index over a member that does not
    ///     exist, rejected by the compiler in a generated file.
    /// </remarks>
    public static readonly DiagnosticDescriptor LogicKeyPartNotFound = DiagnosticFactory.Error(
        "PRAG0636",
        "A domain key names a part that does not exist",
        "[LogicKey] on '{0}' names '{1}', which is neither a property of '{0}' nor a key a relation generates on it",
        "Name a declared property, or the foreign key a [Relation.*] on this entity or on its parent generates.");

    /// <summary>PRAG0637: the domain key declared on the class and on properties at once.</summary>
    /// <remarks>
    ///     One key, one place: two declarations would be two answers to "what identifies this row",
    ///     and whichever one won would be a silent choice.
    /// </remarks>
    public static readonly DiagnosticDescriptor LogicKeyDeclaredTwice = DiagnosticFactory.Error(
        "PRAG0637",
        "The domain key is declared in two places",
        "'{0}' declares [LogicKey] on the class and on one or more properties — one entity has one domain key, declared in one place",
        "Keep the class form when a part is a generated key; otherwise mark the properties and drop the class attribute.");

    /// <summary>
    ///     PRAG0639 — a read entity navigates to a type the reading boundary does not have.
    /// </summary>
    /// <remarks>
    ///     <c>[ReadAccess]</c> brings the owner's configuration for the entity, navigations included,
    ///     and a target the reader neither owns nor reads is ignored by the generated model. The
    ///     navigation is then declared against a type the model does not know: the context builds, and
    ///     the first query that names it fails at run time.
    ///     <para>
    ///         A <b>warning</b> and not an error: the reader may never name that navigation, and an
    ///         application that compiles today must not stop compiling over a query nobody wrote. What
    ///         must not happen is silence.
    ///     </para>
    ///     <para>
    ///         ⚠️ It shipped as Info for exactly one story, because following its own advice broke the
    ///         Showcase a second way — reading <c>Amenity</c> drags in a many-to-many whose join entity
    ///         is still ignored, and this repository builds <c>--warnaserror</c>. That cascade is the
    ///         defect repeating rather than a reason to whisper: Info is the severity documented as off
    ///         by default for <c>PRAG0413</c>, so the four latent instances were reported to nobody.
    ///         The Showcase now reads what its model needs, and the cascade is closed where it was.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor ReadNavigationTargetIsNotRead = DiagnosticFactory.Warning(
        "PRAG0639",
        "The read entity navigates to a type this boundary does not have",
        "'{0}' is read here and its navigation '{1}' points at '{2}', which '{3}' neither owns nor "
        + "reads — the type is ignored in the generated model, so any query naming that navigation "
        + "fails at run time",
        "Add [ReadAccess<{2}>] to the boundary, or do not read through that navigation.");

    /// <summary>
    ///     PRAG0690 — this host stores instants and nothing normalises them to UTC.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Everything that converts an instant on the way out reads the stored value as UTC. The
    ///         convention that makes it so ships in <c>Pragmatic.Temporal.EFCore</c>, which is optional:
    ///         without it a value written with an offset reaches the column as it was, and every later
    ///         conversion works from a number whose meaning depends on who wrote it. No exception, no
    ///         failure — a payload wrong by an offset, twice a year by an hour more.
    ///     </para>
    ///     <para>
    ///         ⚠️ Reported in host mode only. Whether the normalisation is wired is a property of the
    ///         host, not of the module that declares the entity: the same warning on a module would be
    ///         false for every solution whose host references the package.
    ///     </para>
    ///     <para>
    ///         Reported once per host, not once per property. The remedy is a single reference, so
    ///         naming all forty-two properties of a real application would bury it — the message counts
    ///         them and names one.
    ///     </para>
    ///     <para>
    ///         ⚠️ It cannot be a check on the value. A <c>DateTimeKind</c> at run time — from JSON, from
    ///         a calculation, from another system — is not visible to the compiler. <c>PRAG0902</c>
    ///         takes the one static case, a <c>DateTime</c> constructed without a kind.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor InstantsAreNotNormalisedToUtc = DiagnosticFactory.Warning(
        "PRAG0690",
        "Stored instants are not normalised to UTC",
        "This host stores {0} instant properties across {1} entities — '{2}' for one — and does not "
        + "reference Pragmatic.Temporal.EFCore, so nothing normalises what reaches the column while "
        + "the rest of the framework reads it as UTC",
        "Reference Pragmatic.Temporal.EFCore and call UsePragmaticTemporal() on the context, or set "
        + "NormalizeInstantsToUtc to false to say the wall-clock value is the intent.");
}
