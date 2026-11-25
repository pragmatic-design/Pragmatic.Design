using System.Buffers;

namespace Pragmatic.Imaging.Engine;

/// <summary>
///     A <see cref="Memory{T}" /> over bytes already pinned by the caller, so a span can reach a decoder
///     that takes memory (the PNG decoder streams its IDAT data through a reader that outlives one call)
///     without copying the file. Valid only inside the caller's <c>fixed</c> block: it is created there,
///     used there and disposed there.
/// </summary>
internal sealed unsafe class PinnedMemory(byte* pointer, int length) : MemoryManager<byte>
{
    public override Span<byte> GetSpan() => new(pointer, length);

    public override MemoryHandle Pin(int elementIndex = 0) => new(pointer + elementIndex);

    public override void Unpin()
    {
        // The caller's fixed block holds the pin; there is nothing of ours to release.
    }

    protected override void Dispose(bool disposing)
    {
        // Owns nothing: the bytes belong to the caller.
    }
}
