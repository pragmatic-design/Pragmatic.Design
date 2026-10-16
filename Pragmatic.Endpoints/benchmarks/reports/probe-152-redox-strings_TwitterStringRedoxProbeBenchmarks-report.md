# Where RE:Dox's lead on twitter.json is (#152, step 1)

`TwitterStringRedoxProbeBenchmarks`: the 4,754 strings of `twitter.json`, written as one array, by
`Utf8JsonWriter.WriteStringValue` under the response encoder and by RE:Dox under its own settings.
BenchmarkDotNet v0.14.0, Windows 11, .NET 10.0.13, X64 RyuJIT AVX-512, 2026-10-16. Three runs on the same commit;
a cell is `writer / RE:Dox`, in μs.

| Set | Strings | Chars | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|---:|---:|
| all | 4,754 | 137,128 | 192.8 / 144.1 | 179.7 / 130.5 | 189.4 / 149.4 |
| ascii | 3,999 | 91,601 | 72.1 / 67.4 | 62.8 / 78.1 | 81.1 / 64.7 |
| non-ascii | 755 | 45,527 | 100.9 / 65.2 | 93.9 / 59.1 | 103.0 / 61.7 |
| plain | 486 | 24,139 | 35.6 / 27.6 | 32.8 / 25.4 | 32.7 / 26.9 |
| escaped | 269 | 21,388 | 56.3 / 22.5 | 51.0 / 23.3 | 55.3 / 24.6 |

`plain` is non-ASCII text the response encoder writes as it is; `escaped` is non-ASCII text in which it escapes
something. Bytes, from the setup: identical on `ascii` and `plain`; on `escaped` RE:Dox writes 45,404 bytes against
the writer's 45,664, because it escapes less.

## Reading

- **ASCII is not where the lead is.** The two trade places from run to run (72/67, 63/78, 81/65).
- **Plain non-ASCII:** the writer is behind by 6–8 μs on 486 strings, in every run.
- **Escaped text is most of the lead:** 28–34 μs of the 40–50 μs on the whole set, on 269 strings. Part of it is not
  reachable under the host's contract: RE:Dox writes fewer escapes, and the generated writer must write the host's
  bytes.

## What was tried after this, and did not hold

Each candidate wrote the writer's very bytes: it passed an exhaustive test over every UTF-16 code unit, every surrogate
pair and every lone surrogate, against the writer, and each test went red when the part it guards was broken. None is
committed.

| Candidate | all | ascii | plain | escaped |
|---|---:|---:|---:|---:|
| ASCII to the writer; other text checked against a `SearchValues<char>` of the code units the encoder leaves alone, then copied raw | 242 vs 193 | 95 vs 72 | 43 vs 36 | 57 vs 56 |
| One pass: clean ASCII found vectorized, the rest through a 64K-bit table read from the encoder, each escape the encoder's own (`EncodeUtf8`), one `WriteRawValue` per string | 281 vs 180 | 133 vs 63 | 45 vs 33 | 51 vs 51 |
| The same pass into one buffer, one `WriteRawValue` for the whole array (what a run would do) | 213 vs 189 | 86 vs 81 | 40 vs 33 | 49 vs 55 |

The escaping itself is not faster than the writer's on this text. A `SearchValues` search and a transcoding call per
short string (29 chars on average) cost more than they save. A string path ahead of the writer needs one vectorized
loop that transcodes and checks at once, without a call per segment.
