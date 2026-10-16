```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                               | Mean       | Error    | StdDev    | Median     | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------------- |-----------:|---------:|----------:|-----------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| RealProvider                         | 1,100.9 ns | 41.79 ns | 121.91 ns | 1,072.0 ns | 1.10x slower |   0.15x | 0.3891 | 0.0076 |    6560 B |  1.00x more |
| Same                                 | 1,007.0 ns | 28.22 ns |  81.41 ns |   995.5 ns |     baseline |         | 0.3891 | 0.0076 |    6560 B |             |
| ReusedWriter                         |   802.9 ns | 20.42 ns |  60.22 ns |   782.8 ns | 1.26x faster |   0.14x | 0.0496 |      - |     848 B |  7.74x less |
| ReusedWriter_BytesOut                |   668.5 ns | 13.25 ns |  36.49 ns |   658.1 ns | 1.51x faster |   0.14x | 0.0124 |      - |     216 B | 30.37x less |
| ReusedWriter_BytesOut_StackTimestamp |   746.2 ns | 35.46 ns | 104.55 ns |   725.4 ns | 1.38x faster |   0.22x | 0.0086 |      - |     144 B | 45.56x less |
| EmptyWrite                           |   185.6 ns |  6.34 ns |  18.70 ns |   180.3 ns | 5.48x faster |   0.69x | 0.0086 |      - |     144 B | 45.56x less |
| EmptyWrite_NoProperties              |   133.1 ns |  4.49 ns |  12.80 ns |   130.1 ns | 7.63x faster |   0.93x | 0.0052 |      - |      88 B | 74.55x less |
