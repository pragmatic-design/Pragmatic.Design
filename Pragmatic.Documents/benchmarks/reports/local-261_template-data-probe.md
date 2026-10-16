# Template data allocations: what each candidate cost (#261)

Local run, Windows, .NET 10, 2026-10-16. BenchmarkDotNet `--job short`, `TemplateRenderBenchmarks`, the two rows on
the generated accessors. Allocation is deterministic run to run; the times of a short job are indicative only.

- `Read_GeneratedAccessors` (`FieldReads`): 1,000 rows × 10 fields read through `TemplateDataContext.ResolveAsync`,
  one child scope per row.
- `Resolve_GeneratedAccessors` (`DataAccess`): the whole report template resolved into a `DocumentModel`.

## By subtraction, before any fix

Each candidate taken away in a probe, cumulatively, in this order. The probes for 2 and 4 used a static cache keyed
by the text, good enough to measure and not what was kept.

| Step | `Read`, bytes/op | Δ | `Resolve`, bytes/op | Δ |
|---|---:|---:|---:|---:|
| Baseline | 3,266,139 | | 9,408,223 | |
| 1. Child scope: async-source dictionary and `ConcurrentDictionary` cache created per context | 1,592,465 | −1,673,674 (1,674 B per row) | 7,734,474 | −1,673,749 |
| 2. Path sliced and `Split` on every read | 560,466 | −1,031,999 (103 B per read) | 6,702,126 | −1,032,348 |
| 3. Child scope: a `Dictionary` for its one item | 360,482 | −199,984 (200 B per row) | 6,502,140 | −199,986 |
| 4. Each cell's text parsed again on every row | 360,482 | 0 | 3,229,288 | −3,272,852 (3,273 B per row) |

`DictionaryPropertyAccessor` asked first on every property allocates nothing: for a target that is not a dictionary
it is two type checks, and the `foreach` over the accessor list is `List<T>`'s struct enumerator. Not probed.

What is left in `Read` after all four, about 360 B per row, is the child scope object and the boxing of the value
fields (`int`, `decimal`, `DateOnly`) through the `object`-returning accessor API.

## Kept

| | `Read`, bytes/op | `Resolve`, bytes/op | `Read`, mean | `Resolve`, mean |
|---|---:|---:|---:|---:|
| Baseline | 3,266,139 | 9,408,223 | 1.097 ms | 4.843 ms |
| Fixed | 1,472,482 (−55%) | 3,229,400 (−66%) | 0.758 ms | 2.205 ms |

- 1 and 3: the context's collections are created on first use; a child scope holds its one name and value in two
  fields.
- 2: `PropertyAccessExpression` splits its path when the template is parsed, and the evaluator passes the segments
  in. The public `ResolveAsync(string)` still splits, once per call instead of twice; `Read` calls it directly, which
  is why it keeps 103 B per read that `Resolve` no longer pays.
- 4: the resolver keeps each parsed cell expression in a `ConditionalWeakTable` keyed on the template's string
  instance, so an entry lives as long as the template that holds the text.
