# Pragmatic.Result: Benchmark Results

What each `Result`, `VoidResult`, `Maybe` and multi-error operation costs above the code it replaces (#132):
every row of `ResultBenchmarks` has a hand-written twin doing the same work without the types, measured in the
same run.

```bash
cd Pragmatic.Result/benchmarks/Pragmatic.Result.Benchmarks
dotnet run -c Release -- --filter "*ResultBenchmarks*"           # every row and its twin
dotnet run -c Release -- --filter "*ResultBenchmarks*" --disasm  # with each row's machine code
dotnet run -c Release -- verify                                  # the checks below, nothing timed
```

⚠️ The filter between quotes: unquoted, the shell expands `*ResultBenchmarks*` into the file names of the folder,
BenchmarkDotNet finds no benchmark, and exits 0.

**The numbers that decide are the GitHub runner's**, from the benchmarks workflow (2026-10-10): `runner-1_*`
(the first push of #132), and `runner-2-main_*` against `runner-2_*` (main and #132 in the same run, the A/B).
Its deviations are 1–5 ns per batch. The development machine (AMD Ryzen 9 9950X, .NET 10.0.12) gave the
disassembly (`twins-2_ResultBenchmarks-asm.md`) but not the times: there, a row and a twin that compile to the
same instructions read up to 38% apart, and several rows changed sign between runs (`twins-1_*`, `twins-2_*`,
`twins-3-launches_*`). On the runner the same pair reads level.

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

## Results on the runner

Each row's mean over its twin's, in the same run (above 1: the row costs more). Run 1 is before the change to
the multi-error `Match` below, run 2 after it.

| Row | Run 1 | Run 2 | Reading |
|---|---:|---:|---|
| `IsSuccess_Check` | 0.96 | 1.01 | level |
| `TryGetValue_Pattern` | 0.99 | 1.02 | level |
| `Maybe_CreateNone` | 1.01 | 1.00 | level — the same machine code as its twin |
| `Maybe_GetValueOrDefault` | 1.00 | 1.00 | level |
| `VoidResult_Failure` | 1.00 | 1.00 | level |
| `CreateFailure` / `_Implicit` | 1.00 / 1.00 | 1.12 / 1.11 | level, then 12% — the twin moved (3,209 → 2,877 ns), not the row |
| `Value_DirectAccess` | 1.03 | 1.06 | a check |
| `VoidResult_Success` | 1.02 | 1.06 | twice the bytes |
| `Maybe_CreateSome` | 0.92 | 1.09 | level to 9% |
| `CreateSuccess` / `_Implicit` | 1.12 / 1.03 | 1.12 / 1.17 | 3–17% |
| `Map_SingleTransform` | 1.07 | 1.11 | a delegate |
| `Bind_SingleOperation` | 1.12 | 1.23 | a delegate, a copy, a check |
| `MultiError_CreateSuccess` | 1.29 | 1.27 | 27% |
| `Map_ChainedTransforms` | 1.39 | 1.31 | three delegates |
| `MultiError_Match` | **4.29** | **1.66** | delegates; a jump table in run 1 |
| `Match_Pattern` | 1.67 | 1.79 | delegates |
| `Maybe_Match` | 1.79 | 1.78 | delegates |

No row allocates more than its twin: the only rows that allocate, `Map`'s, allocate the strings they produce,
16,832 and 20,608 B on both sides.

## What the disassembly says

- **`Match`, `Maybe.Match`, `MultiError.Match`, `Map`, `Bind`**: the JIT already inlines the delegates (guarded
  devirtualization) and drops the null checks on them. What remains per element is loading the lambdas the C#
  compiler caches in static fields, testing them for null, and the guard comparing each delegate with the one it
  inlined: the cost of an API that takes a delegate, which hand-written code does not pay. `TryGetValue` is level
  with the twin, and is the way to read a result without it.
- **`Bind`**: the result also goes through a temporary on the stack before it is copied into place, and the
  failure path checks the error for null — on purpose: it keeps a `default(Result)` from propagating as
  `Failure(null)`.
- **`Value`**: one compare and branch per element on `IsSuccess`, which the twin does not have: `Value` throws on
  a failure instead of returning a default. The analyzer (`PRAG0001`) warns where it is read unchecked.
- **`VoidResult.Success`**: the struct is the error and a `bool`, sixteen bytes per element where the twin writes
  eight. The `bool` is what keeps `default(VoidResult)` from reading as a success.
- **The creations, `MultiError_CreateSuccess` included**: the same instructions as the twin plus one byte store
  per element, the discriminator (`IsSuccess = true` for a result, `_index = 0` for a multi-error one). It is the
  field that tells a success from a failure without reading the payload, and what makes a `default` value
  neither: the twin, a tuple, has no such field and reads success from a null error.
- **`Maybe.None`**: the row and its twin compile to the same instructions, store for store, and read level on
  the runner — the control for the rest of the table.

## The change kept

**`MultiError.Match` without its jump table.** The generated `Match` of a multi-error result switched on the
result's index, and with three cases the JIT compiled the switch to a jump table: one indirect jump per call,
predicted unevenly. It is now a chain of tests, success first (`ResultVariantTemplate`). In the same run on the
runner (the A/B): **3,458.7 ns ±969.4 (median 4,118.2) on main against 1,484.7 ns ±0.8**, while `Match_Pattern`
(1,365.0 → 1,364.3) and `Maybe_CreateNone` (650.1 → 652.3) did not move. The development machine could not tell
the two apart.

## Tried and not kept

**`Bind` and `Map` written with `if`/`return` instead of `?:`**: the same machine code, 365 bytes either way, and
the same time (1,356 against 1,355 ns, development machine).

## Not covered

- **Async operations, `Tap`, `Ensure`, `Recover`, error aggregation**: no row measures them yet.
- **The other generated switches** (`Map`, `Bind`, the `Action` `Match` of a multi-error result, the multi-error
  `VoidResult`s) have the same shape as the `Match` that was changed and no row of their own: left as they are
  until one measures them.
