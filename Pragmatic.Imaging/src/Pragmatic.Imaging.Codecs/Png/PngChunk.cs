namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>Where one chunk's data sits in the encoded bytes.</summary>
internal readonly record struct PngChunk(uint Type, int DataOffset, int DataLength)
{
    public const uint Ihdr = 0x49484452; // "IHDR"
    public const uint Plte = 0x504C5445; // "PLTE"
    public const uint Trns = 0x74524E53; // "tRNS"
    public const uint Idat = 0x49444154; // "IDAT"
    public const uint Iend = 0x49454E44; // "IEND"
}
