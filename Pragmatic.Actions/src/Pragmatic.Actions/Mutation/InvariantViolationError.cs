using Pragmatic.Result;

namespace Pragmatic.Actions.Mutation;

/// <summary>
///     Error returned when an aggregate invariant (an entity method marked <c>[Invariant]</c>) does not
///     hold after a mutation is applied. The mutation is rejected before it is persisted.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It is a refusal of the <b>generated operations</b>: a mutation, and a <c>[DomainAction]</c> for the
///         rows it loaded (each row of a <c>[LoadEntities]</c> list, named by <see cref="EntityType" /> and
///         <see cref="EntityId" />). A repository <c>Add</c>/<c>Remove</c> called by hand writes with none of
///         those methods run.
///     </para>
///     <para>
///         It derives from <see cref="Error" /> so its message can be translated. The localization
///         resolver asks <c>context is Error</c> and reads that error's <see cref="Error.MessageKey" />;
///         while this type implemented <c>IError</c> directly it took the fallback derived from
///         <see cref="Code" /> — <c>error.invariant.violation</c>, one text for every invariant in the
///         application, which cannot say which rule refused. An <c>[Invariant]</c>'s sentence was the only
///         text in the stack with no way to be localized.
///     </para>
/// </remarks>
/// <param name="Invariant">The name of the violated invariant method.</param>
/// <param name="Message">Human-readable explanation (from <c>[Invariant("…")]</c>, or a default).</param>
/// <param name="DeclaredMessageKey">
///     The key <c>[Invariant(MessageKey = …)]</c> names, or <see langword="null" /> when the rule names
///     none — and then the key is the one derived from <see cref="Code" />, as before.
/// </param>
public sealed record InvariantViolationError(string Invariant, string Message, string? DeclaredMessageKey = null)
    : Error
{
    /// <summary>The code every invariant reports when its rule declares none.</summary>
    public const string DefaultCode = "INVARIANT_VIOLATION";

    /// <inheritdoc />
    /// <remarks>
    ///     <c>[Invariant(ErrorCode = …)]</c> when the rule declares one, so a client can tell two rules
    ///     apart without reading their sentences; <see cref="DefaultCode" /> otherwise.
    /// </remarks>
    public override string Code => DeclaredCode ?? DefaultCode;

    /// <summary>The code <c>[Invariant(ErrorCode = …)]</c> declares, or <see langword="null" /> for the shared one.</summary>
    public string? DeclaredCode { get; init; }

    /// <summary>
    ///     The entity member the rule is about, by its C# name (<c>[Invariant(Member = nameof(…))]</c>), or
    ///     <see langword="null" /> when it names none.
    /// </summary>
    public string? Member { get; init; }

    /// <inheritdoc />
    public override int StatusCode => 422;

    /// <inheritdoc />
    /// <remarks>
    ///     The message with each <c>{Name}</c> replaced by that parameter's value, the substitution a
    ///     translation of <see cref="MessageKey" /> gets from the localizer, so the untranslated sentence
    ///     says the same as a translated one.
    /// </remarks>
    public override string Title => string.IsNullOrEmpty(Message)
        ? $"Invariant '{Invariant}' was violated."
        : Format(Message, Parameters);

    /// <inheritdoc />
    public override string MessageKey => DeclaredMessageKey ?? base.MessageKey;

    /// <summary>
    ///     The entity whose row broke the rule, when the operation changed several rows of it — a <c>[LoadEntities]</c> list — and the
    ///     caller has to be told which one; <see langword="null" /> otherwise.
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>The key of the row that broke the rule, with <see cref="EntityType" />; <see langword="null" /> otherwise.</summary>
    public string? EntityId { get; init; }

    /// <inheritdoc />
    /// <remarks>
    ///     With a <see cref="Member" />, the refusal also carries <c>member</c> and an <c>errors</c> map
    ///     keyed by it — <c>{"total": ["error.order_over_limit"]}</c> — the shape a property rule's failure
    ///     has, so a client that marks fields from <c>errors</c> marks this one without knowing it came
    ///     from an invariant. The name is camel-cased as the payload's names are.
    /// </remarks>
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (EntityType is not null) extensions["entityType"] = EntityType;
        if (EntityId is not null) extensions["entityId"] = EntityId;

        if (string.IsNullOrEmpty(Member))
            return;

        var wire = char.ToLowerInvariant(Member![0]) + Member[1..];
        extensions["member"] = wire;
        extensions["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal) { [wire] = [MessageKey] };
    }

    private static string Format(string message, IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is not { Count: > 0 })
            return message;

        foreach (var (name, value) in parameters)
        {
            message = message.Replace(
                "{" + name + "}",
                Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
                StringComparison.Ordinal);
        }

        return message;
    }
}
