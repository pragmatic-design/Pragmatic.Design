using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Transforms;

/// <summary>
///     Extracts <see cref="MessageHandlerModel"/> from [MessageHandler]-decorated classes.
/// </summary>
internal static class MessageHandlerTransform
{
    public static MessageHandlerModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Find IMessageHandler<T> implementation to get message type
        var messageTypeFqn = (string?)null;
        var messageTypeShortName = (string?)null;
        var messageIsDomainEvent = false;

        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface is { IsGenericType: true, Name: "IMessageHandler", TypeArguments.Length: 1 })
            {
                var messageType = iface.TypeArguments[0];
                messageTypeFqn = messageType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                messageTypeShortName = messageType.Name;

                // Whether the in-process bridge to IDomainEventHandler<T> may be emitted for this type.
                // Matched on the containing namespace as well: an application's own IDomainEvent must not
                // decide whether code that names Pragmatic's is generated — the lesson PRAG2750's
                // base-class probe carries.
                messageIsDomainEvent = messageType.AllInterfaces.Any(candidate =>
                    candidate.Name == "IDomainEvent"
                    && candidate.ContainingNamespace?.ToDisplayString() == "Pragmatic.Events");

                // ⚠️ No [PartitionKey] scan here. One that sat here read the
                // attribute off the type this handler CONSUMES, so the resolver it produced was
                // generated in the consuming assembly — while TransportAwareMessageBus resolves
                // IPartitionKeyResolver in the assembly that PUBLISHES. DeclaredPartitionKeyTransform
                // reads it where the message is declared, which serves both.

                // [NotLogged] symbol scan: collect the
                // properties to redact when this payload is serialized into logs/audit trails.
                // Walks the whole reachable graph — the audit serializer redacts per graph NODE, so a
                // nested type needs its own map entry or its [NotLogged] members leak in full.

                break;
            }
        }

        // Must implement IMessageHandler<T>
        if (messageTypeFqn is null)
            return null;

        // Read [MessageHandler] attribute
        var attr = context.Attributes[0];
        var order = attr.GetNamedArgument<int>("Order");

        // Read [Retry]. The declaration is shared with jobs; the numbers below are this engine's.
        //
        // ⚠️ `?.X ?? fallback` reads as «default when unwritten» and is not: GetNamedArgument<int>
        // returns 0 for an argument the caller did not write, and 0 is not null, so the fallback
        // fires only when the whole attribute is absent. A bare [Retry] on a handler would therefore
        // ask for zero attempts with a zero delay — well formed, and nothing to see.
        var retryAttr = symbol.GetAttribute("Pragmatic.Resilience.Attributes.RetryAttribute");
        var hasRetry = retryAttr is not null;
        var retryMaxAttempts = retryAttr.GetWrittenIntOrDefault("MaxAttempts", 3);
        var retryStrategy = retryAttr.GetWrittenIntOrDefault("Strategy", BackoffStrategyValues.Exponential);
        var retryBaseDelayMs = retryAttr.GetWrittenIntOrDefault("BaseDelayMs", 200);

        // Read [CircuitBreaker] if present
        var cbAttr = symbol.GetAttribute("Pragmatic.Resilience.Attributes.CircuitBreakerAttribute");
        var hasCb = cbAttr is not null;
        var cbFailureThreshold = cbAttr.GetWrittenIntOrDefault("FailureThreshold", 5);
        var cbBreakDuration = cbAttr.GetWrittenIntOrDefault("BreakDurationSeconds", 30);

        // Read [Timeout] if present
        var timeoutAttr = symbol.GetAttribute("Pragmatic.Resilience.Attributes.TimeoutAttribute");
        var hasTimeout = timeoutAttr is not null;
        var timeoutSeconds = timeoutAttr.GetWrittenIntOrDefault("TimeoutSeconds", 30);

        // Read [Redelivery] if present
        var redeliveryAttr = symbol.GetAttribute("Pragmatic.Messaging.Attributes.RedeliveryAttribute");
        var hasRedelivery = redeliveryAttr is not null;
        var redeliveryMaxAttempts = redeliveryAttr?.GetNamedArgument<int>("MaxAttempts") ?? 3;
        var redeliveryBaseDelaySeconds = redeliveryAttr?.GetNamedArgument<int>("BaseDelaySeconds") ?? 30;
        if (redeliveryMaxAttempts <= 0) redeliveryMaxAttempts = 3;
        if (redeliveryBaseDelaySeconds <= 0) redeliveryBaseDelaySeconds = 30;

        // Read [ConcurrencyLimit] if present (ctor arg)
        var concurrencyAttr = symbol.GetAttribute("Pragmatic.Messaging.Attributes.ConcurrencyLimitAttribute");
        var hasConcurrencyLimit = concurrencyAttr is not null;
        var maxConcurrent = concurrencyAttr?.ConstructorArguments.Length > 0
            ? concurrencyAttr.ConstructorArguments[0].Value as int? ?? 1
            : 1;
        if (maxConcurrent <= 0) maxConcurrent = 1;

        // Read [RateLimit] if present (ctor arg + PeriodSeconds)
        var rateAttr = symbol.GetAttribute("Pragmatic.Messaging.Attributes.RateLimitAttribute");
        var hasRateLimit = rateAttr is not null;
        var ratePermits = rateAttr?.ConstructorArguments.Length > 0
            ? rateAttr.ConstructorArguments[0].Value as int? ?? 1
            : 1;
        var ratePeriodSeconds = rateAttr?.GetNamedArgument<int>("PeriodSeconds") ?? 0;
        if (ratePeriodSeconds <= 0) ratePeriodSeconds = 1;
        if (ratePermits <= 0) ratePermits = 1;

        // Read [OnBus] if present
        var onBusAttr = symbol.GetAttribute("Pragmatic.Messaging.Attributes.OnBusAttribute");
        var busName = onBusAttr?.ConstructorArguments.Length > 0
            ? onBusAttr.ConstructorArguments[0].Value as string
            : null;

        return new MessageHandlerModel
        {
            Namespace = symbol.GetNamespaceOrEmpty(),
            TypeName = symbol.Name,
            Accessibility = symbol.GetAccessibilityKeyword(),
            TypeKind = symbol.GetTypeKindKeyword(),
            IsPartial = symbol.IsPartial(),
            AssemblyName = symbol.ContainingAssembly?.Name ?? "",
            MessageTypeFqn = messageTypeFqn,
            MessageTypeShortName = messageTypeShortName!,
            MessageIsDomainEvent = messageIsDomainEvent,
            Order = order,
            HasRetry = hasRetry,
            RetryMaxAttempts = retryMaxAttempts,
            RetryStrategy = retryStrategy,
            RetryBaseDelayMs = retryBaseDelayMs,
            HasCircuitBreaker = hasCb,
            CbFailureThreshold = cbFailureThreshold,
            CbBreakDurationSeconds = cbBreakDuration,
            HasTimeout = hasTimeout,
            TimeoutSeconds = timeoutSeconds,
            HasRedelivery = hasRedelivery,
            RedeliveryMaxAttempts = redeliveryMaxAttempts,
            RedeliveryBaseDelaySeconds = redeliveryBaseDelaySeconds,
            HasConcurrencyLimit = hasConcurrencyLimit,
            MaxConcurrent = maxConcurrent,
            HasRateLimit = hasRateLimit,
            RatePermitsPerPeriod = ratePermits,
            RatePeriodSeconds = ratePeriodSeconds,
            BusName = busName,
            LocationInfo = LocationInfo.From(symbol.Locations.Length > 0 ? symbol.Locations[0] : null),
        };
    }

    /// <summary>Bounds the graph walk so a deep or self-referencing model cannot stall generation.</summary>
    private const int MaxRedactionDepth = 8;

    /// <summary>
    ///     Collects every type reachable from the message that declares <c>[NotLogged]</c> members.
    ///     The audit serializer redacts per graph NODE (it looks the current node's type up in the
    ///     generated map), so each nested type needs its own entry — otherwise a secret on a nested
    ///     object is serialized in full. Cycles are cut by the visited set.
    /// </summary>
    /// <summary>
    ///     The name the property SERIALIZES to — the <c>[JsonPropertyName]</c> value when present, else
    ///     the CLR name. The audit redactor matches on the serialized name (<c>JsonPropertyInfo.Name</c>),
    ///     so a renamed <c>[NotLogged]</c> member must be listed by its JSON name or it would leak in full.
    ///     Un-renamed members are still matched case-insensitively downstream, which absorbs a camelCase
    ///     naming policy, so emitting the CLR name for them is correct.
    /// </summary>
    private static string EffectiveJsonName(IPropertySymbol property)
    {
        foreach (var attr in property.GetAttributes())
        {
            if (attr.AttributeClass is { Name: "JsonPropertyNameAttribute" } cls
                && cls.ContainingNamespace?.ToDisplayString() == "System.Text.Json.Serialization"
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is string jsonName
                && !string.IsNullOrEmpty(jsonName))
            {
                return jsonName;
            }
        }

        return property.Name;
    }

    /// <summary>Unwraps arrays, <c>Nullable&lt;T&gt;</c> and generic collections to the element type.</summary>
    private static ITypeSymbol Unwrap(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return Unwrap(array.ElementType);

        if (type is INamedTypeSymbol { IsGenericType: true } generic)
        {
            if (generic.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                return Unwrap(generic.TypeArguments[0]);

            // Collections: walk the element type. The LAST type argument covers both IEnumerable<T>
            // and Dictionary<TKey,TValue> (whose values carry the payload).
            var isEnumerable = generic.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                || generic.AllInterfaces.Any(i =>
                    i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
            if (isEnumerable && generic.TypeArguments.Length > 0)
                return Unwrap(generic.TypeArguments[generic.TypeArguments.Length - 1]);
        }

        return type;
    }

    /// <summary>
    ///     Only user-defined types are walked — descending into BCL types would be unbounded noise
    ///     and they never carry <c>[NotLogged]</c>.
    /// </summary>
    private static bool IsUserDefined(INamedTypeSymbol type)
        => type.SpecialType == SpecialType.None
            && type.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface
            && type.ContainingNamespace?.ToDisplayString() is { } ns
            && !ns.StartsWith("System", StringComparison.Ordinal)
            && !ns.StartsWith("Microsoft", StringComparison.Ordinal);
}
