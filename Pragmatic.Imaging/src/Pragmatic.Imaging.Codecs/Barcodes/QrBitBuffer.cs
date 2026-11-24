namespace Pragmatic.Imaging.Codecs.Barcodes;

/// <summary>The data bits of a QR symbol, most significant bit first, in a buffer of the symbol's data codewords.</summary>
internal sealed class QrBitBuffer(int capacityBytes)
{
    private readonly byte[] _bytes = new byte[capacityBytes];

    public int Length { get; private set; }

    public int Capacity => _bytes.Length * 8;

    public void Append(int value, int count)
    {
        for (var i = count - 1; i >= 0; i--, Length++)
        {
            if (((value >> i) & 1) != 0)
                _bytes[Length >> 3] |= (byte)(0x80 >> (Length & 7));
        }
    }

    public byte[] ToBytes() => _bytes;
}
