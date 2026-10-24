namespace PragmaticDocumentSamples;

/// <summary>The checks of one group of samples on one backend: each printed, each counted, the failures counted apart.</summary>
internal sealed class Checks(string backend)
{
    private SampleRun _run;

    public SampleRun Run => _run;

    public void Check(string what, bool ok)
    {
        Console.WriteLine($"{backend} {what}: {(ok ? "ok" : "FAIL")}");
        _run += new SampleRun(1, ok ? 0 : 1);
    }

    public void Add(SampleRun run) => _run += run;
}
