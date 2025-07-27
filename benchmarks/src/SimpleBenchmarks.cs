using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Shelltrac.Benchmarks;

namespace Shelltrac.Benchmarks
{
    /// <summary>
    /// Simplified performance tests that work with the public API
    /// </summary>
    public class SimpleBenchmarks
    {
        public static void RunAll()
        {
            Console.WriteLine("===== Shelltrac Performance Benchmarks =====");
            Console.WriteLine($"Runtime: {Environment.Version}");
            Console.WriteLine($"Processors: {Environment.ProcessorCount}");
            Console.WriteLine();

            var allResults = new List<PerformanceTestFramework.TestResult>();
            var gitCommit = PerformanceTestFramework.GetGitCommit();

            allResults.AddRange(RunBasicExecutionTests());
            allResults.AddRange(RunParsingTests());
            allResults.AddRange(RunVariableScopeTests());
            allResults.AddRange(RunParallelExecutionTests());

            // Tag all results with git commit and timestamp
            foreach (var result in allResults)
            {
                result.GitCommit = gitCommit;
            }

            // Save results to timestamped file
            PerformanceTestFramework.SaveResults(allResults);

            // Compare with baseline if it exists
            var baselinePath = Path.Combine("benchmarks", "baseline_benchmark.json");
            if (File.Exists(baselinePath))
            {
                PerformanceTestFramework.CompareWithBaseline(allResults, baselinePath);
            }
            else
            {
                Console.WriteLine("\n💡 Tip: Save these results as baseline with:");
                Console.WriteLine("   cp benchmarks/benchmark_results_*.json benchmarks/baseline_benchmark.json");
            }
        }

        private static List<PerformanceTestFramework.TestResult> RunBasicExecutionTests()
        {
            Console.WriteLine("=== Basic Execution Tests ===");
            var results = new List<PerformanceTestFramework.TestResult>();

            var simpleAssignment = PerformanceTestFramework.RunTest(
                "Simple Variable Assignment",
                () => {
                    var source = "let x = 42";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 1000
            );

            var shellCommand = PerformanceTestFramework.RunTest(
                "Shell Command Execution",
                () => {
                    var source = "sh \"echo 'test'\"";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 50
            );

            var stringInterpolation = PerformanceTestFramework.RunTest(
                "String Interpolation",
                () => {
                    var source = "let name = \"world\"\nlet greeting = \"Hello #{name}!\"";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 500
            );

            Console.WriteLine($"Simple Assignment:     {simpleAssignment}");
            Console.WriteLine($"Shell Command:         {shellCommand}");
            Console.WriteLine($"String Interpolation:  {stringInterpolation}");
            Console.WriteLine();

            results.Add(simpleAssignment);
            results.Add(shellCommand);
            results.Add(stringInterpolation);
            return results;
        }

        private static List<PerformanceTestFramework.TestResult> RunParsingTests()
        {
            Console.WriteLine("=== Parsing Performance Tests ===");
            var results = new List<PerformanceTestFramework.TestResult>();

            var simpleExpression = PerformanceTestFramework.RunTest(
                "Simple Expression Parsing",
                () => {
                    var source = "let x = 1 + 2 * 3";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    parser.ParseProgram();
                },
                iterations: 2000
            );

            var complexScript = PerformanceTestFramework.RunTest(
                "Complex Script Parsing",
                () => {
                    var source = @"
                        fn calculate(x) {
                            if x > 10 {
                                return x * 2
                            } else {
                                return x + 5
                            }
                        }
                        let result = calculate(15)
                    ";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    parser.ParseProgram();
                },
                iterations: 500
            );

            var tokenScanning = PerformanceTestFramework.RunTest(
                "Token Scanning",
                () => {
                    var source = "let x = \"Hello #{name} with #{count} items\"";
                    var scanner = new DSLScanner(source);
                    scanner.ScanTokens();
                },
                iterations: 5000
            );

            Console.WriteLine($"Simple Expression:     {simpleExpression}");
            Console.WriteLine($"Complex Script:        {complexScript}");
            Console.WriteLine($"Token Scanning:        {tokenScanning}");
            Console.WriteLine();

            results.Add(simpleExpression);
            results.Add(complexScript);
            results.Add(tokenScanning);
            return results;
        }

        private static List<PerformanceTestFramework.TestResult> RunVariableScopeTests()
        {
            Console.WriteLine("=== Variable Scope Performance Tests ===");
            var results = new List<PerformanceTestFramework.TestResult>();

            var executor = new Executor("benchmark", "");

            var variableAccess = PerformanceTestFramework.RunTest(
                "Variable Access",
                () => {
                    executor.Context.SetCurrentScopeVariable("test_var", "test_value");
                    var result = executor.Context.LookupVariable("test_var");
                },
                iterations: 10000
            );

            var scopeOperations = PerformanceTestFramework.RunTest(
                "Scope Push/Pop Operations",
                () => {
                    executor.Context.PushScope();
                    executor.Context.SetCurrentScopeVariable("scoped_var", 42);
                    executor.Context.LookupVariable("scoped_var");
                    executor.Context.PopScope();
                },
                iterations: 5000
            );

            var nestedScopes = PerformanceTestFramework.RunTest(
                "Nested Scope Access",
                () => {
                    executor.Context.PushScope();
                    executor.Context.SetCurrentScopeVariable("outer", "outer_value");
                    executor.Context.PushScope();
                    executor.Context.SetCurrentScopeVariable("inner", "inner_value");
                    var result = executor.Context.LookupVariable("outer");
                    executor.Context.PopScope();
                    executor.Context.PopScope();
                },
                iterations: 2000
            );

            Console.WriteLine($"Variable Access:       {variableAccess}");
            Console.WriteLine($"Scope Operations:      {scopeOperations}");
            Console.WriteLine($"Nested Scope Access:   {nestedScopes}");
            Console.WriteLine();

            results.Add(variableAccess);
            results.Add(scopeOperations);
            results.Add(nestedScopes);
            return results;
        }

        private static List<PerformanceTestFramework.TestResult> RunParallelExecutionTests()
        {
            Console.WriteLine("=== Parallel Execution Performance Tests ===");
            var results = new List<PerformanceTestFramework.TestResult>();

            var smallParallel = PerformanceTestFramework.RunTest(
                "Small Parallel Loop (10 items)",
                () => {
                    var source = @"
                        let numbers = 1..10
                        let results = parallel for i in numbers {
                            return i * 2
                        }
                    ";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 50
            );

            var mediumParallel = PerformanceTestFramework.RunTest(
                "Medium Parallel Loop (100 items)",
                () => {
                    var source = @"
                        let numbers = 1..100
                        let results = parallel for i in numbers {
                            return i * 2
                        }
                    ";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 20
            );

            var sequentialVsParallel = PerformanceTestFramework.RunTest(
                "Sequential Loop (50 items)",
                () => {
                    var source = @"
                        let numbers = 1..50
                        let results = for i in numbers {
                            return i * 2
                        }
                    ";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 50
            );

            Console.WriteLine($"Small Parallel (10):   {smallParallel}");
            Console.WriteLine($"Medium Parallel (100): {mediumParallel}");
            Console.WriteLine($"Sequential (50):       {sequentialVsParallel}");
            Console.WriteLine();

            // Compare parallel vs sequential
            var parallelFor50 = PerformanceTestFramework.RunTest(
                "Parallel Loop (50 items)",
                () => {
                    var source = @"
                        let numbers = 1..50
                        let results = parallel for i in numbers {
                            return i * 2
                        }
                    ";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 30
            );

            Console.WriteLine("=== Sequential vs Parallel Comparison ===");
            PerformanceTestFramework.CompareResults(sequentialVsParallel, parallelFor50);

            results.Add(smallParallel);
            results.Add(mediumParallel);
            results.Add(sequentialVsParallel);
            results.Add(parallelFor50);
            return results;
        }

        /// <summary>
        /// Quick smoke test to verify everything works
        /// </summary>
        public static void RunSmokeTest()
        {
            Console.WriteLine("Running smoke test...");

            try
            {
                var result = PerformanceTestFramework.RunTest(
                    "Smoke Test",
                    () => {
                        var source = "let x = 42";
                        var scanner = new DSLScanner(source);
                        var tokens = scanner.ScanTokens();
                        var parser = new DSLParser(tokens, source);
                        var program = parser.ParseProgram();
                        var executor = new Executor("benchmark", "");
                        executor.Execute(program);
                    },
                    iterations: 5,
                    warmupIterations: 1
                );

                if (result.Error == null)
                {
                    Console.WriteLine("✓ Smoke test passed");
                    Console.WriteLine($"  Basic execution: {result.AverageTime.TotalMilliseconds:F2}ms");
                }
                else
                {
                    Console.WriteLine($"✗ Smoke test failed: {result.Error.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Smoke test error: {ex.Message}");
            }
        }
    }
}