namespace PragmaticDocumentSamples;

/// <summary>What a run of the samples checked, and how many of those checks failed.</summary>
public readonly record struct SampleRun(int Checks, int Failures)
{
    public static SampleRun operator +(SampleRun left, SampleRun right) => new(left.Checks + right.Checks, left.Failures + right.Failures);
}
