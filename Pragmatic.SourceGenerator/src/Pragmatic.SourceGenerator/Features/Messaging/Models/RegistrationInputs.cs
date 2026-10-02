using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;

namespace Pragmatic.SourceGenerator.Features.Messaging.Models;

/// <summary>
///     Everything an assembly's messaging registration is made of, and the one answer to whether it has
///     one.
/// </summary>
/// <remarks>
///     One model for the registration file, the metadata that makes a host call it, and the local
///     registration of a host that declares its handlers itself — because three conditions for one
///     question is how a <c>[RequestHandler]</c> came to be registered in a file nobody called.
/// </remarks>
internal sealed record RegistrationInputs(
    EquatableArray<MessageHandlerModel> HandlerModels,
    DetectedFeatures Features,
    EquatableArray<MessageTypeModel> RegistryTypeModels,
    EquatableArray<RequestHandlerModel> RequestHandlerModels,
    EquatableArray<PartitionKeyModel> PartitionKeyModels,
    EquatableArray<MessageMiddlewareModel> MiddlewareModels,
    string AssemblyName)
{
    public ImmutableArray<MessageHandlerModel> Handlers => HandlerModels.AsImmutableArray();

    public ImmutableArray<MessageTypeModel> RegistryTypes => RegistryTypeModels.AsImmutableArray();

    public ImmutableArray<RequestHandlerModel> RequestHandlers => RequestHandlerModels.AsImmutableArray();

    public ImmutableArray<PartitionKeyModel> PartitionKeys => PartitionKeyModels.AsImmutableArray();

    public ImmutableArray<MessageMiddlewareModel> Middlewares => MiddlewareModels.AsImmutableArray();

    /// <summary>
    ///     Whether the registration has anything in it: a handler, a request handler, a type to resolve,
    ///     a middleware or a partition key. Any one of them alone is registered only from there.
    /// </summary>
    public bool HasAnythingToRegister
        => !HandlerModels.IsDefaultOrEmpty || !RequestHandlerModels.IsDefaultOrEmpty
           || !RegistryTypeModels.IsDefaultOrEmpty || !MiddlewareModels.IsDefaultOrEmpty
           || !PartitionKeyModels.IsDefaultOrEmpty;

    /// <summary>The namespace the registration is declared in, and the one the host is told to call.</summary>
    public string RegistrationNamespace
        => HandlerRegistrationTemplate.NamespaceFor(Handlers, RegistryTypes, AssemblyName);
}
