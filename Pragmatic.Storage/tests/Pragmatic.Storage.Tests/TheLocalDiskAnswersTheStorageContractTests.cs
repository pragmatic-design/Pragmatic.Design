using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Conformance;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     <see cref="LocalDiskFileStorage" /> against the shared storage contract.
/// </summary>
/// <remarks>
///     The cases are in <see cref="AForeignUriIsACallerErrorContract" />, written once for every
///     provider. The trap for this one is leniency: a URI it did not write must not read as a file
///     it merely does not have.
/// </remarks>
public sealed class TheLocalDiskAnswersTheStorageContractTests : AForeignUriIsACallerErrorContract
{
    /// <remarks>
    ///     The directory is never created: none of these cases writes, and the constructor does not
    ///     touch the disk. A root that exists would make the cases depend on what is in it.
    /// </remarks>
    protected override IFileStorage CreateStorage()
        => new LocalDiskFileStorage(
            Path.Combine(Path.GetTempPath(), $"pragmatic-storage-contract-{Guid.NewGuid():N}"),
            NullLogger<LocalDiskFileStorage>.Instance);

    /// <remarks>Absolute, which this provider's own URIs never are — they are relative to be servable.</remarks>
    protected override Uri ForeignUri { get; } = new("mem://docs/note.txt");
}
