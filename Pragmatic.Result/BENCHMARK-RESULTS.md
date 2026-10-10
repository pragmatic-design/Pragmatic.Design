# Pragmatic.Result: Benchmark Results

What each `Result`, `VoidResult`, `Maybe` and multi-error operation costs above the code it replaces (#132):
every row of `ResultBenchmarks` has a hand-written twin doing the same work without the types, measured in the
same run. The numbers come from three runs on 2026-10-10 of:

```bash
cd Pragmatic.Result/benchmarks/Pragmatic.Result.Benchmarks
dotnet run -c Release -- --filter "*ResultBenchmarks*"                    # runs 1 and 2
dotnet run -c Release -- --filter "*ResultBenchmarks*" --launchCount 3    # run 3
dotnet run -c Release -- verify                                           # the checks below, nothing timed
```

⚠️ The filter between quotes: unquoted, the shell expands `*ResultBenchmarks*` into the file names of the folder,
BenchmarkDotNet finds no benchmark, and exits 0.

| | |
|---|---|
| Machine | AMD Ryzen 9 9950X (reported by BenchmarkDotNet as "Unknown processor") |
| Runtime | .NET 10.0.12, X64 RyuJIT AVX-512 |
| Harness | BenchmarkDotNet 0.14.0, default job, `[MemoryDiagnoser]`; `--disasm` for the disassembly |

The reports are in [`benchmarks/reports/`](benchmarks/reports/): `twins-1_*`, `twins-2_*` (with the
disassembly of every row, `twins-2_ResultBenchmarks-asm.md`) and `twins-3-launches_*`.

## The twins

A row walks 1,024 inputs (every fourth a failure); its twin walks the same inputs written as hand-written code
would hold them:

| Type | Twin |
|---|---|
| `Result<int, E>` | `(int Value, E? Error)` |
| `VoidResult<E>` | `E?` |
| `Maybe<int>` | `(bool HasValue, int Value)` |
| `Result<int, E1, E2>` | `(int Value, E1? A, E2? B)` |
| `Match`, `Map`, `Bind` | the branch they stand for, without a delegate |

The explicit and implicit creations share a twin, as do `TryGetValue` and `Match` (`Twin_ValueOrZero`) and
`Maybe.GetValueOrDefault` and `Maybe.Match` (`Twin_MaybeValueOrZero`). Before anything is timed the setup checks
that every row computes what its name says and that every twin produces what its rows produce; a twin that did
less would make its row look slower than it is.

## Results

Each row's mean over its twin's, in the same run (above 1: the row costs more). Run 3 runs each benchmark in
three processes.

| Row | Run 1 | Run 2 | Run 3 | Reading |
|---|---:|---:|---:|---|
| `CreateSuccess` | 0.87 | 0.86 | 1.18 | sign changes between runs |
| `CreateSuccess_Implicit` | 0.86 | 0.86 | 1.18 | sign changes between runs |
| `CreateFailure` | 1.19 | 1.18 | 0.80 | sign changes between runs |
| `CreateFailure_Implicit` | 1.18 | 1.17 | 0.90 | sign changes between runs |
| `IsSuccess_Check` | 0.87 | 0.90 | 0.79 | faster |
| `Value_DirectAccess` | 1.42 | 1.43 | 1.07 | slower: a check |
| `TryGetValue_Pattern` | 0.91 | 0.91 | 0.95 | faster |
| `Match_Pattern` | 1.72 | 1.72 | 1.60 | slower: delegates |
| `Map_SingleTransform` | 1.01 | 0.98 | 0.75 | level or faster |
| `Map_ChainedTransforms` | 1.39 | 1.51 | 1.10 | slower: delegates |
| `Bind_SingleOperation` | 1.21 | 1.22 | 1.15 | slower: a copy, a check |
| `VoidResult_Success` | 1.66 | 1.67 | 1.59 | slower: twice the bytes |
| `VoidResult_Failure` | 1.18 | 1.18 | 1.31 | slower: one more store |
| `Maybe_CreateSome` | 1.02 | 1.02 | 1.35 | level in two runs of three |
| `Maybe_CreateNone` | 1.38 | 1.35 | 1.13 | **the same machine code as its twin** |
| `Maybe_GetValueOrDefault` | 0.70 | 0.68 | 1.16 | sign changes between runs |
| `Maybe_Match` | 1.64 | 1.59 | 2.71 | slower: delegates |
| `MultiError_CreateSuccess` | 0.70 | 0.71 | 1.58 | sign changes between runs |
| `MultiError_Match` | 1.93 | 1.91 | 2.12 | slower: delegates, a jump table |

No row allocates more than its twin: the only rows that allocate, `Map`'s, allocate the strings they produce,
16,832 and 20,608 B on both sides.

## What the disassembly says

The rows slower in every run, at the source (`twins-2_ResultBenchmarks-asm.md`):

- **`Value`**: one compare and branch per element on `IsSuccess`, which the twin does not have: `Value` throws
  on a failure instead of returning a default. The analyzer (`PRAG0001`) and `TryGetValue`, faster than the twin
  in every run, are the ways to read a value without it.
- **`Match`, `Maybe.Match`, `MultiError.Match`, `Map` chained**: the JIT already inlines the delegates (guarded
  devirtualization) and drops the null checks on them. What remains per element is loading the lambdas the C#
  compiler caches in static fields, testing them for null, and the guard comparing each delegate with the one it
  inlined: the cost of an API that takes a delegate, which hand-written code does not pay. `MultiError.Match`
  also dispatches on its index through a jump table, one indirect jump per element.
- **`Bind`**: the result goes through a temporary on the stack before it is copied into place, and the failure
  path checks the error for null — on purpose: it keeps a `default(Result)` from propagating as `Failure(null)`.
- **`VoidResult.Success` and `Failure`**: the struct is the error and a `bool`, sixteen bytes per element where the
  twin writes eight, and one more store. The `bool` is what keeps `default(VoidResult)` from reading as a
  success.
- **`Maybe.None`**: the row and its twin compile to the same instructions, store for store, and the row is still
  13–38% slower. The difference is where the arrays they write and the loops land in memory, not the code: it is
  the size of what the harness cannot tell apart at this scale, and why the rows whose sign changes say nothing.

## What was tried and not kept

- **`Bind` and `Map` written with `if`/`return` instead of `?:`**: the same machine code, 365 bytes either way,
  and the same time (1,356 against 1,355 ns).
- **`MultiError.Match` as a chain of tests instead of a switch**: the jump table went from the disassembly, and the
  row read 760, 801 and 842 ns in three runs against 813 and 816 before — inside the drift between runs, so not a
  change its row justifies.

## Not covered

- **The cost per operation below the drift.** These operations cost 0.2–1.8 ns each (`Map`'s 4–6 ns are mostly
  the string it formats); the drift between runs of identical code is of the same size. A difference this harness cannot reproduce in every run is not reported as
  an overhead.
- **Async operations, `Tap`, `Ensure`, `Recover`, error aggregation**: no row measures them yet.
