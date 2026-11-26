namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The rate-distortion account of a choice (libwebp's <c>VP8ModeScore</c>): distortion as the sum of
///     squared errors, the rate of the levels and of the mode headers in 1/256 bit, and the score that weighs
///     them, lower being better.
/// </summary>
internal struct Vp8Score
{
    /// <summary>How much a unit of distortion weighs against a unit of rate times lambda.</summary>
    private const int DistortionWeight = 256;

    public long Distortion;
    public long Rate;
    public long Header;
    public long Value;

    public static Vp8Score Worst => new() { Value = long.MaxValue };

    public void Weigh(int lambda) => Value = (Rate + Header) * lambda + DistortionWeight * Distortion;

    public void Add(in Vp8Score other)
    {
        Distortion += other.Distortion;
        Rate += other.Rate;
        Header += other.Header;
        Value += other.Value;
    }
}
