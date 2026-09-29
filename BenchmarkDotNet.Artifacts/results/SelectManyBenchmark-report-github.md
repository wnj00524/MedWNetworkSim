```

BenchmarkDotNet v0.13.12, Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 8.0.24 (8.0.2426.7010), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.24 (8.0.2426.7010), X64 RyuJIT AVX2


```
| Method         | Mean     | Error     | StdDev    | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------- |---------:|----------:|----------:|------:|-------:|-------:|----------:|------------:|
| LinqSelectMany | 4.463 μs | 0.0487 μs | 0.0432 μs |  1.00 | 0.4425 | 0.0076 |  10.28 KB |        1.00 |
| ManualLoop     | 3.459 μs | 0.0470 μs | 0.0439 μs |  0.77 | 0.1717 |      - |      4 KB |        0.39 |
