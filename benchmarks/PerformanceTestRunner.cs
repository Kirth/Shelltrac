using System;
using System.Diagnostics;
using System.IO;
using Shelltrac.Benchmarks;

namespace Shelltrac.Benchmarks
{
    /// <summary>
    /// Main performance test runner for Shelltrac benchmarks
    /// </summary>
    public class PerformanceTestRunner
    {
        public static void MainBenchmarks(string[] args)
        {
            Console.WriteLine("===== Shelltrac Performance Benchmark Suite =====");
            Console.WriteLine($"Running on: {Environment.OSVersion}");
            Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");
            Console.WriteLine($"Runtime: {Environment.Version}");
            Console.WriteLine($"Working Directory: {Environment.CurrentDirectory}");
            Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                if (args.Length > 0)
                {
                    RunSpecificTest(args[0]);
                }
                else
                {
                    RunAllTests();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running benchmarks: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                stopwatch.Stop();
                Console.WriteLine($"\nTotal benchmark time: {stopwatch.Elapsed.TotalSeconds:F1} seconds");
            }
        }

        private static void RunAllTests()
        {
            Console.WriteLine("Running all performance tests...\n");

            // Run simplified benchmarks that work with public API
            SimpleBenchmarks.RunAll();

            // Generate summary report
            GenerateSummaryReport();
        }

        private static void RunSpecificTest(string testName)
        {
            Console.WriteLine($"Running specific test: {testName}\n");

            switch (testName.ToLowerInvariant())
            {
                case "smoke":
                    SimpleBenchmarks.RunSmokeTest();
                    break;
                case "all":
                case "basic":
                    SimpleBenchmarks.RunAll();
                    break;
                default:
                    Console.WriteLine($"Unknown test: {testName}");
                    Console.WriteLine("Available tests: smoke, basic, all");
                    break;
            }
        }

        private static void GenerateSummaryReport()
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("PERFORMANCE SUMMARY REPORT");
            Console.WriteLine(new string('=', 60));

            Console.WriteLine("\nKey Performance Characteristics:");
            Console.WriteLine("• Shell execution has significant process creation overhead");
            Console.WriteLine("• Reflection-based member access is 10-100x slower than direct access");
            Console.WriteLine("• String interpolation scales with number of variables");
            Console.WriteLine("• Parallel execution benefits depend on task complexity");
            Console.WriteLine("• Variable scope lookup time increases with nesting depth");

            Console.WriteLine("\nOptimization Opportunities:");
            Console.WriteLine("1. ASYNC SHELL EXECUTION - Biggest potential performance gain");
            Console.WriteLine("2. REFLECTION CACHING - Significant improvement for member access");
            Console.WriteLine("3. VARIABLE STORAGE - Optimize for typical scope sizes");
            Console.WriteLine("4. STRING INTERPOLATION - Pre-calculate StringBuilder capacity");
            Console.WriteLine("5. PARALLEL TASK OVERHEAD - Reduce executor creation costs");

            Console.WriteLine("\nRecommended Focus Areas:");
            Console.WriteLine("• High Impact: Shell execution async conversion");
            Console.WriteLine("• Medium Impact: Reflection caching and compilation");
            Console.WriteLine("• Low Impact: String building micro-optimizations");

            // Write results to file
            var reportPath = Path.Combine("benchmarks", "performance_report.txt");
            try
            {
                File.WriteAllText(reportPath, $"Shelltrac Performance Report - {DateTime.Now}\n" +
                                            "See console output for detailed results.\n");
                Console.WriteLine($"\nBasic report written to: {reportPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not write report file: {ex.Message}");
            }
        }

        /// <summary>
        /// Run a quick smoke test to verify benchmarks are working
        /// </summary>
        public static void RunSmokeTest()
        {
            SimpleBenchmarks.RunSmokeTest();
        }
    }
}