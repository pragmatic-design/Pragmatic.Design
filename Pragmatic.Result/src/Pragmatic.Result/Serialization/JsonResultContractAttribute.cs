namespace Pragmatic.Result.Serialization;

/// <summary>
///     Declares that a closed <c>Result</c> type crosses a JSON boundary, so the source generator emits
///     a typed converter for it.
/// </summary>
/// <typeparam name="TResult">
///     The closed result type, e.g. <c>Result&lt;OrderDto, NotFoundError, ConflictError&gt;</c>.
/// </typeparam>
/// <remarks>
///     <para>
///         A declaration is needed because there is nothing for the generator to infer from. No part of
///         the framework puts a <c>Result</c> on the wire as a result: endpoints unwrap it into the value
///         or a ProblemDetails, and the remote invoker sends its own envelope and rebuilds the result on
///         arrival with typed factories. The set of results that get serialized is whatever the
///         application decides to serialize, and only the application can say so.
///     </para>
///     <para>
///         This replaces registering <c>ResultJsonConverterFactory</c>, which built converters with
///         <c>MakeGenericType</c> and read the result's factory methods through <c>MethodInfo.Invoke</c> —
///         never AOT-safe, and reflective on every runtime. Nothing is inferred at run time here: the
///         wire format carries <c>isSuccess</c>, so value and error never have to be told apart by
///         guessing, and the concrete error type comes from its own discriminator through
///         <c>ErrorTypeRegistry</c>.
///     </para>
///     <example>
///         <code>
/// [assembly: JsonResultContract&lt;Result&lt;OrderDto, NotFoundError, ConflictError&gt;&gt;]
///         </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class JsonResultContractAttribute<TResult> : Attribute
    where TResult : IResultBase;
