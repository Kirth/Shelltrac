using System;
using System.Collections.Generic;
using System.Diagnostics;
using Shelltrac.Benchmarks;

namespace Shelltrac.Benchmarks
{
    /// <summary>
    /// Performance tests for reflection and member access optimization
    /// </summary>
    public class ReflectionBenchmarks
    {
        public static void RunAll()
        {
            Console.WriteLine("=== Reflection Performance Tests ===");
            var results = new List<PerformanceTestFramework.TestResult>();

            var stringMemberAccess = PerformanceTestFramework.RunTest(
                "String Member Access (Length)",
                () => {
                    var source = "let str = \"hello world\"\nlet length = str.Length";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 1000
            );

            var stringMethodAccess = PerformanceTestFramework.RunTest(
                "String Method Access (ToUpper)",
                () => {
                    var source = "let str = \"hello\"\nlet upper = str.ToUpper()";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 1000
            );

            var multipleStringAccess = PerformanceTestFramework.RunTest(
                "Multiple String Member Access",
                () => {
                    var source = @"
                        let str = ""hello world""
                        let length = str.Length
                        let upper = str.ToUpper()
                        let lower = str.ToLower()
                        let trimmed = str.Trim()
                    ";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 500
            );

            var repeatedMemberAccess = PerformanceTestFramework.RunTest(
                "Repeated Member Access (Same Property)",
                () => {
                    var source = @"
                        let str = ""test string""
                        for i in 1..10 {
                            let len = str.Length
                        }
                    ";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 200
            );

            var mixedTypeAccess = PerformanceTestFramework.RunTest(
                "Mixed Type Member Access",
                () => {
                    var source = @"
                        let str = ""hello""
                        let len = str.Length
                        let list = 1..5
                        let count = list.Count()
                    ";
                    var scanner = new DSLScanner(source);
                    var tokens = scanner.ScanTokens();
                    var parser = new DSLParser(tokens, source);
                    var program = parser.ParseProgram();
                    var executor = new Executor("benchmark", "");
                    executor.Execute(program);
                },
                iterations: 500
            );

            Console.WriteLine($"String Member Access:     {stringMemberAccess}");
            Console.WriteLine($"String Method Access:     {stringMethodAccess}");
            Console.WriteLine($"Multiple String Access:   {multipleStringAccess}");
            Console.WriteLine($"Repeated Member Access:   {repeatedMemberAccess}");
            Console.WriteLine($"Mixed Type Access:        {mixedTypeAccess}");
            Console.WriteLine();

            results.Add(stringMemberAccess);
            results.Add(stringMethodAccess);
            results.Add(multipleStringAccess);
            results.Add(repeatedMemberAccess);
            results.Add(mixedTypeAccess);
        }

        /// <summary>
        /// Quick smoke test for reflection optimization
        /// </summary>
        public static void RunSmokeTest()
        {
            Console.WriteLine("Running reflection smoke test...");

            try
            {
                var result = PerformanceTestFramework.RunTest(
                    "Reflection Smoke Test",
                    () => {
                        var source = "let str = \"test\"\nlet len = str.Length";
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
                    Console.WriteLine("✓ Reflection smoke test passed");
                    Console.WriteLine($"  Member access execution: {result.AverageTime.TotalMilliseconds:F2}ms");
                }
                else
                {
                    Console.WriteLine($"✗ Reflection smoke test failed: {result.Error.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Reflection smoke test error: {ex.Message}");
            }
        }
    }
}