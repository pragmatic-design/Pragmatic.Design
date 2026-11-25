namespace Pragmatic.Imaging.Codecs.Metadata;

/// <summary>
///     One marker segment of a JPEG: where it starts (its first <c>0xFF</c>), how many bytes it spans,
///     marker and length included, and where its payload sits.
/// </summary>
internal readonly record struct JpegSegment(byte Marker, int Offset, int Length, int DataOffset, int DataLength);
