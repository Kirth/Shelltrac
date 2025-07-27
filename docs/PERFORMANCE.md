# Performance Tracking

Shelltrac includes comprehensive performance instrumentation that can be enabled at compile time to help identify performance bottlenecks.

## Usage

### Normal Build (No Performance Tracking)
```bash
dotnet build
dotnet run test_cache.s
```

Output shows only normal program execution without performance data.

### Performance Tracking Build
```bash
dotnet build -p:DefineConstants=PERF_TRACKING
dotnet run test_cache.s
```

Or use the convenience script:
```bash
./build_perf.sh
```

## Performance Output

When performance tracking is enabled, you'll see detailed timing information:

### Scanner Performance
```
[PERF] Scanning breakdown - Total: 30ms
[PERF]   Strings: 26 tokens, 12ms
[PERF]   Identifiers: 74 tokens, 15ms
[PERF]   Numbers: 15 tokens, 0ms
[PERF]   Other: 233 tokens
```

### Parser Performance
```
[PERF] Parsing 32 statements took 14ms
```

### Execution Performance
```
[PERF] Execution breakdown - Total: 64ms
[PERF]   Tasks: 0, Functions: 3, Events: 0, Regular: 29
[PERF]   Slow stmt: InvocationStmt took 36ms
[PERF]   Slow stmt: FunctionStmt took 12ms
[PERF]   Slow stmt: VarDeclStmt took 10ms
```

### Detailed Operation Tracking
For operations that exceed performance thresholds, you'll see detailed breakdowns:

```
[PERF] Slow invocation 'log': total=35ms (eval=6ms, exec=29ms)
[PERF] Slow member access 'TimeLiteralExpr.TotalMilliseconds': total=120ms (eval=1ms, bind=118ms)
```
