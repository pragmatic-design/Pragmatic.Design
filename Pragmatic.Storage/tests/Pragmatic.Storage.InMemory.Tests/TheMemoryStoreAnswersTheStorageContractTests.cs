using Pragmatic.Storage.Conformance;

namespace Pragmatic.Storage.InMemory.Tests;

/// <summary>
///     <see cref="InMemoryFileStorage" /> against the shared storage contract.
/// </summary>
/// <remarks>
///     The cases are in <see cref="AForeignUriIsACallerErrorContract" />, written once for every
///     provider. This one was the other half of the divergence the contract closes — the controls that
///     say the change is narrow live in <c>AForeignUriIsACallerErrorTests</c>, where a real save is
///     available.
/// </remarks>
public sealed class TheMemoryStoreAnswersTheStorageContractTests : AForeignUriIsACallerErrorContract
{
    protected override IFileStorage CreateStorage() => new InMemoryFileStorage();

    /// <remarks>Relative, which is the shape <c>LocalDiskFileStorage</c> returns.</remarks>
    protected override Uri ForeignUri { get; } = new("/files/docs/note.txt", UriKind.Relative);
}
