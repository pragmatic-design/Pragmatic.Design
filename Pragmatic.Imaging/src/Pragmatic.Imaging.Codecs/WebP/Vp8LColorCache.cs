namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>The lossless format's colour cache: recent colours by a multiplicative hash (RFC 9649, section 5.2.3).</summary>
internal sealed class Vp8LColorCache(int bits)
{
    private readonly uint[] _colours = new uint[1 << bits];
    private readonly int _shift = 32 - bits;

    public void Insert(uint argb) => _colours[(int)((0x1E35A7BDu * argb) >> _shift)] = argb;

    public uint Lookup(int index) => _colours[index];
}
