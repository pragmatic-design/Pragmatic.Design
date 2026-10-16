# Peak memory of one render (#144)

`dotnet run -c Release --project Pragmatic.Documents/benchmarks/Pragmatic.Documents.Benchmarks -- memory`

Windows 11, .NET 10. Each format and size runs in a process of its own, after a 100-row warm-up render
and a full collection. The managed heap is sampled every millisecond, so its peak is a lower bound.

## Before: main at 080fb493 (before #274 and #276)

| Format | Rows | Managed heap peak above start | Process peak working set |
|---|---:|---:|---:|
| pdf | 1,000 | 14,300,384 | 302,682,112 |
| pdf | 10,000 | 79,002,312 | 2,269,995,008 |
| pdf | 50,000 | 385,087,624 | 11,178,065,920 |
| xlsx | 1,000 | 3,550,592 | 40,325,120 |
| xlsx | 10,000 | 19,761,064 | 66,301,952 |
| xlsx | 50,000 | 81,288,128 | 138,072,064 |

## After: main at 8d863649 (with #274 and #276), 2026-10-16

| Format | Rows | Output bytes | Managed heap peak above start | Process peak working set |
|---|---:|---:|---:|---:|
| pdf | 1,000 | 155,215 | 6,679,920 | 276,680,704 |
| pdf | 10,000 | 1,311,100 | 68,461,176 | 2,254,172,160 |
| pdf | 50,000 | 7,622,112 | 376,935,336 | 11,170,193,408 |
| xlsx | 1,000 | 88,056 | 3,554,200 | 40,243,200 |
| xlsx | 10,000 | 892,731 | 19,195,824 | 66,473,984 |
| xlsx | 50,000 | 4,409,079 | 81,151,856 | 138,133,504 |

## Reading

- **pdf:** #274 halved the managed peak of a small report (14.3 → 6.7 MB at 1,000 rows), since the
  template no longer reallocates per row; at 50,000 rows the managed heap is dominated by the model and
  the markup, and moved by 2%.
- The working set is unchanged within 1% at every size. It grows by about 223 KB per row, outside the
  managed heap: the native engine. #276 gives that memory back when a render returns; it does not lower
  the peak during one. That is #279.
- **xlsx:** linear, about 1.5 KB of managed heap per row, unchanged. The model holds every row.
