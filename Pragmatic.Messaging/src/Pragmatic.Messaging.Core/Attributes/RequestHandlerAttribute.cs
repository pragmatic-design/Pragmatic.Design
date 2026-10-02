namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Marks a class as a request handler for source generator discovery.
///     The class must implement <see cref="IRequestHandler{TRequest, TResponse}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequestHandlerAttribute : Attribute;
