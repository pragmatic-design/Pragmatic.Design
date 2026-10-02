using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     One property per validation rule, and an endpoint that runs them all.
/// </summary>
/// <remarks>
///     <para>
///         The generator renders <b>31</b> rules plus <c>[Required]</c> and <c>[ValidateElements]</c>:
///         the list is complete. The question left is the other one, which no snapshot can ask —
///         <b>are they executed?</b> A rule that is generated and never invoked looks, in production,
///         like valid data.
///     </para>
///     <para>
///         The rules all sit on the same type on purpose. A valid body is sent once, then <b>one
///         property at a time</b> is broken: each case thus has its own built-in control, because the
///         same body that passes with the good value must be refused with the bad one, and the only
///         difference is the field under test.
///     </para>
///     <para>
///         ⚠️ <c>[AllowAnonymous]</c>: validation is what is measured here, and a permission in between
///         would refuse before the rules come into play — a 403 that looks like a 422 and proves
///         something else.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/rules")]
public partial class CheckEveryRuleAction : DomainAction<string>
{
    // Presence
    [Required] public required string Present { get; init; }
    [NotEmpty] public required string NotEmpty { get; init; }
    [NotWhiteSpace] public required string NotBlank { get; init; }

    // String
    [MinLength(3)] public required string AtLeastThree { get; init; }
    [MaxLength(5)] public required string AtMostFive { get; init; }
    [Length(2, 4)] public required string BetweenTwoAndFour { get; init; }

    // Format
    [Email] public required string Email { get; init; }
    [Phone] public required string Phone { get; init; }
    [Url] public required string Url { get; init; }
    [Regex("^[A-Z]{3}$")] public required string ThreeCapitals { get; init; }
    [CreditCard] public required string Card { get; init; }
    [Guid] public required string GuidText { get; init; }

    // Numeric
    [Range(1, 10)] public required int InRange { get; init; }
    [Positive] public required int Positive { get; init; }
    [Negative] public required int Negative { get; init; }
    [GreaterThan(10)] public required int OverTen { get; init; }
    [GreaterThanOrEqual(10)] public required int TenOrOver { get; init; }
    [LessThan(10)] public required int UnderTen { get; init; }
    [LessThanOrEqual(10)] public required int TenOrUnder { get; init; }

    // Collection
    [MinCount(1)] public required List<string> AtLeastOne { get; init; }
    // ⚠️ The three length rules promise «a string or a collection»: here the collection half, which
    // must count elements, not characters.
    [MinLength(2)] public required List<string> LongEnough { get; init; }
    [MaxLength(2)] public required List<string> NotTooLong { get; init; }
    [Length(1, 3)] public required List<string> WithinBounds { get; init; }
    [MaxCount(2)] public required List<string> AtMostTwo { get; init; }
    // ⚠️ [NotEmpty] on a collection: a pattern with members no collection has both of —
    // `{ Count: 0 } or { Length: 0 }` — would compile on no type.
    [NotEmpty] public required List<string> Filled { get; init; }
    [Count(1, 3)] public required List<string> OneToThree { get; init; }

    // Comparison with another property by name
    // ⚠️ [EqualTo] and [NotEqualTo] take the NAME of another property, not a value: they exist for
    // password confirmation. Passing a value, the generator would emit that text as an identifier —
    // `Equals(MustEqual, fixed)` — and the error would come out as CS0103 inside a generated file the
    // author cannot open.
    [EqualTo(nameof(Mirror))] public required string MustEqual { get; init; }
    [NotEqualTo(nameof(Mirror))] public required string MustDiffer { get; init; }

    /// <summary>The term of comparison for the two rules above.</summary>
    public required string Mirror { get; init; }

    // Comparison with a set of values
    [OneOf("red", "green")] public required string FromTheList { get; init; }

    // Comparison with another property
    [GreaterThanProperty(nameof(Floor))] public required int AboveFloor { get; init; }
    [LessThanProperty(nameof(Ceiling))] public required int BelowCeiling { get; init; }
    [GreaterThanOrEqualProperty(nameof(Floor))] public required int FloorOrAbove { get; init; }
    [LessThanOrEqualProperty(nameof(Ceiling))] public required int CeilingOrBelow { get; init; }

    /// <summary>The reference of <c>AboveFloor</c>: without it, that rule has nothing to compare against.</summary>
    public required int Floor { get; init; }

    /// <summary>The reference of <c>BelowCeiling</c>.</summary>
    public required int Ceiling { get; init; }

    // Conditional presence
    [RequiredIf(nameof(Trigger), true)] public string? NeededWhenTriggered { get; init; }
    [RequiredIfNot(nameof(Trigger), true)] public string? NeededWhenNotTriggered { get; init; }

    /// <summary>The condition of the two rules above.</summary>
    public required bool Trigger { get; init; }

    // Dates
    [FutureDate] public required DateTime Later { get; init; }
    [PastDate] public required DateTime Earlier { get; init; }

    // A wire name different from the C# one
    // ⚠️ The case the rest does not cover: the error must name `people`, which is what the caller sent
    // and what the document publishes — not `Roster`, which only whoever reads the source knows. It is
    // the one half the writer cannot derive by itself.
    [MinCount(1)]
    [System.Text.Json.Serialization.JsonPropertyName("people")]
    public required List<string> Roster { get; init; }

    // Enum
    // ⚠️ A real enum: [ValidEnum] on a type that is not one would emit Enum.IsDefined<T> with a T that
    // does not satisfy the constraint — CS0453 inside the generated file. PRAG0220 reports the attribute
    // on the wrong type instead.
    [ValidEnum] public required Shade Tint { get; init; }

    /// <summary>Answers only if every rule passed: the body here does not matter.</summary>
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<string, IError>.Success("every rule passed"));
}

/// <summary>An enum, for the rule that wants one.</summary>
public enum Shade
{
    /// <summary>The first.</summary>
    Light = 1,

    /// <summary>The second.</summary>
    Dark = 2,
}
