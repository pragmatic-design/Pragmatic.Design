using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Storage.Conformance;

/// <summary>
///     Every provider refuses a URI in another provider's shape, from all four reads, the same way.
/// </summary>
/// <remarks>
///     <para>
///         The URI shape is provider-defined, so a URI one provider wrote names nothing in another.
///         Providers that answer that in different ways — some throwing, some returning
///         <see langword="null" /> as if the file were merely absent — break an application moving
///         between them, which is the one thing this seam exists to make safe.
///     </para>
///     <para>
///         ⚠️ Written once and inherited rather than copied seven times. A copy per provider is a
///         contract that can drift silently: the eighth provider is added, its suite is written from
///         the nearest neighbour, and whichever case that neighbour happened to be missing is missing
///         again. Inheriting means a provider that does not satisfy the contract fails to compile a
///         subclass or fails the case, and there is one place to change when the contract does.
///     </para>
///     <para>
///         ⚠️ The <em>controls</em> are deliberately not here. "A URI it did write still resolves" and
///         "one it wrote and deleted answers null" need a working backend, which for a cloud provider
///         means the mocks that live in its own suite. Those stay where the mocks are; what is shared
///         is the case that needs nothing but the URI.
///     </para>
/// </remarks>
public abstract class AForeignUriIsACallerErrorContract
{
    /// <summary>A provider instance that has never been asked to store anything.</summary>
    /// <remarks>
    ///     Called per case, so one case cannot leave state behind for the next. Nothing here saves a
    ///     file, so a provider needs no backend for these — only enough to be constructed.
    /// </remarks>
    protected abstract IFileStorage CreateStorage();

    /// <summary>A URI in some other provider's shape.</summary>
    /// <remarks>
    ///     Each provider names its own, because "foreign" is relative: <c>mem://c/x.txt</c> is foreign
    ///     to the disk and native to the memory store. A provider that returned a URI of its own here
    ///     would be asserting the opposite of the contract, which is why this is abstract rather than a
    ///     constant.
    /// </remarks>
    protected abstract Uri ForeignUri { get; }

    /// <summary>Reading it.</summary>
    [Fact]
    public async Task GetAsync_AForeignUri_IsRefused()
        => await ((Func<Task>)(() => CreateStorage().GetAsync(ForeignUri)))
            .Should().ThrowAsync<ArgumentException>();

    /// <summary>Asking whether it is there.</summary>
    /// <remarks>
    ///     ⚠️ The one most likely to be forgotten, and the most misleading if it is: a provider that
    ///     answered <see langword="false" /> here would report a wiring mistake as an ordinary absence,
    ///     and the caller would take the branch it takes for a file that is gone.
    /// </remarks>
    [Fact]
    public async Task ExistsAsync_AForeignUri_IsRefused()
        => await ((Func<Task>)(() => CreateStorage().ExistsAsync(ForeignUri)))
            .Should().ThrowAsync<ArgumentException>();

    /// <summary>Deleting it.</summary>
    /// <remarks>
    ///     A delete of a missing file is a no-op by contract, so a provider that treated a foreign URI
    ///     as missing would silently do nothing — the failure with no symptom at all.
    /// </remarks>
    [Fact]
    public async Task DeleteAsync_AForeignUri_IsRefused()
        => await ((Func<Task>)(() => CreateStorage().DeleteAsync(ForeignUri)))
            .Should().ThrowAsync<ArgumentException>();

    /// <summary>And asking for its metadata.</summary>
    /// <remarks>
    ///     ⚠️ <see cref="IFileInfoProvider" /> is optional on the seam, and all seven shipped providers
    ///     implement it. The cast is asserted rather than tested with <c>is</c>: skipping a provider
    ///     that does not implement it would make "every provider refuses" and "every provider that
    ///     happens to answer metadata refuses" the same green, and they are not the same claim.
    /// </remarks>
    [Fact]
    public async Task GetInfoAsync_AForeignUri_IsRefused()
    {
        var info = CreateStorage().Should().BeAssignableTo<IFileInfoProvider>(
            "every shipped provider answers metadata — one that does not has to change this contract, "
            + "not be quietly skipped by it").Which;

        await ((Func<Task>)(() => info.GetInfoAsync(ForeignUri))).Should().ThrowAsync<ArgumentException>();
    }
}
