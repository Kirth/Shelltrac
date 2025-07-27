using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Shelltrac.Benchmarks
{
    [JsonSerializable(typeof(List<BenchmarkResult>))]
    [JsonSerializable(typeof(BenchmarkResult))]
    public partial class BenchmarkJsonContext : JsonSerializerContext
    {
    }

    public class BenchmarkResult
    {
        public string TestName { get; set; } = "";
        public double AverageTimeMs { get; set; }
        public double MinTimeMs { get; set; }
        public double MaxTimeMs { get; set; }
        public long AverageMemoryBytes { get; set; }
        public long PeakMemoryBytes { get; set; }
        public int Iterations { get; set; }
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string GitCommit { get; set; } = "";
        public string TestCategory { get; set; } = "";
    }

    public class BenchmarkFile
    {
        public string FileName { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string GitCommit { get; set; } = "";
        public List<BenchmarkResult> Results { get; set; } = new();
    }

    public static class BenchmarkComparer
    {
        public static void CompareBenchmarks()
        {
            Console.WriteLine("===== Benchmark Comparison Report =====\n");

            var benchmarkFiles = LoadAllBenchmarkFiles();
            if (!benchmarkFiles.Any())
            {
                Console.WriteLine("No benchmark files found in benchmarks/ directory.");
                return;
            }

            Console.WriteLine($"Found {benchmarkFiles.Count} benchmark files:\n");

            // Display overview table
            DisplayOverviewTable(benchmarkFiles);

            Console.WriteLine("\n" + new string('=', 120));
            Console.WriteLine("DETAILED COMPARISON BY TEST");
            Console.WriteLine(new string('=', 120));

            // Get all unique test names
            var allTestNames = benchmarkFiles
                .SelectMany(f => f.Results.Select(r => r.TestName))
                .Distinct()
                .OrderBy(name => name)
                .ToList();

            foreach (var testName in allTestNames)
            {
                DisplayTestComparison(testName, benchmarkFiles);
            }
        }

        private static List<BenchmarkFile> LoadAllBenchmarkFiles()
        {
            var benchmarkFiles = new List<BenchmarkFile>();
            var benchmarksDir = Path.Combine(Environment.CurrentDirectory, "benchmarks");
            
            if (!Directory.Exists(benchmarksDir))
            {
                return benchmarkFiles;
            }

            var jsonFiles = Directory.GetFiles(benchmarksDir, "*.json")
                .Where(f => Path.GetFileName(f).StartsWith("benchmark_results_") || 
                           Path.GetFileName(f) == "baseline_benchmark.json")
                .OrderBy(f => f);

            foreach (var file in jsonFiles)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var results = JsonSerializer.Deserialize(content, BenchmarkJsonContext.Default.ListBenchmarkResult);

                    if (results != null && results.Any())
                    {
                        var benchmarkFile = new BenchmarkFile
                        {
                            FileName = Path.GetFileName(file),
                            Results = results,
                            Timestamp = results.First().Timestamp,
                            GitCommit = results.First().GitCommit
                        };
                        benchmarkFiles.Add(benchmarkFile);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading {file}: {ex.Message}");
                }
            }

            return benchmarkFiles.OrderBy(f => f.Timestamp).ToList();
        }

        private static void DisplayOverviewTable(List<BenchmarkFile> benchmarkFiles)
        {
            Console.WriteLine("┌─────────────────────────────────────────┬─────────────┬──────────┬──────────┬─────────────┐");
            Console.WriteLine("│ File                                    │ Date        │ Commit   │ Tests    │ Avg Time    │");
            Console.WriteLine("├─────────────────────────────────────────┼─────────────┼──────────┼──────────┼─────────────┤");

            foreach (var file in benchmarkFiles)
            {
                var avgTime = file.Results.Where(r => !r.HasError).Average(r => r.AverageTimeMs);
                var testCount = file.Results.Count;
                var dateStr = file.Timestamp.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture);
                var commitStr = file.GitCommit.Length > 7 ? file.GitCommit.Substring(0, 7) : file.GitCommit;

                Console.WriteLine($"│ {file.FileName,-39} │ {dateStr,-11} │ {commitStr,-8} │ {testCount,8} │ {avgTime,8:F2}ms │");
            }

            Console.WriteLine("└─────────────────────────────────────────┴─────────────┴──────────┴──────────┴─────────────┘");
        }

        private static void DisplayTestComparison(string testName, List<BenchmarkFile> benchmarkFiles)
        {
            Console.WriteLine($"\n━━━ {testName} ━━━");

            var testResults = benchmarkFiles
                .Select(f => new
                {
                    File = f,
                    Result = f.Results.FirstOrDefault(r => r.TestName == testName)
                })
                .Where(x => x.Result != null)
                .ToList();

            if (!testResults.Any())
            {
                Console.WriteLine("  No results found for this test.");
                return;
            }

            // Table header
            Console.WriteLine("┌─────────────┬────────────┬────────────┬─────────────┬──────────────┬────────────┐");
            Console.WriteLine("│ Date        │ Commit     │ Avg Time   │ Memory (KB) │ Iterations   │ Status     │");
            Console.WriteLine("├─────────────┼────────────┼────────────┼─────────────┼──────────────┼────────────┤");

            var baseline = testResults.First().Result;
            foreach (var item in testResults)
            {
                var result = item.Result!;
                var dateStr = item.File.Timestamp.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture);
                var commitStr = item.File.GitCommit.Length > 7 ? item.File.GitCommit.Substring(0, 7) : item.File.GitCommit;
                var timeStr = result.HasError ? "ERROR" : $"{result.AverageTimeMs:F3}ms";
                var memoryStr = result.HasError ? "ERROR" : $"{result.AverageMemoryBytes / 1024.0:F1}";
                var iterStr = result.HasError ? "ERROR" : result.Iterations.ToString();
                var statusStr = result.HasError ? "FAILED" : "OK";

                // Add performance comparison indicator
                if (!result.HasError && !baseline.HasError && item != testResults.First())
                {
                    var improvement = ((baseline.AverageTimeMs - result.AverageTimeMs) / baseline.AverageTimeMs) * 100;
                    if (Math.Abs(improvement) > 5) // Only show significant changes
                    {
                        var arrow = improvement > 0 ? "↑" : "↓";
                        var color = improvement > 0 ? "\u001b[32m" : "\u001b[31m"; // Green/Red
                        statusStr = $"{color}{arrow}{Math.Abs(improvement):F0}%\u001b[0m";
                    }
                }

                Console.WriteLine($"│ {dateStr,-11} │ {commitStr,-10} │ {timeStr,10} │ {memoryStr,11} │ {iterStr,12} │ {statusStr,-10} │");
            }

            Console.WriteLine("└─────────────┴────────────┴────────────┴─────────────┴──────────────┴────────────┘");

            // Show trend summary
            var validResults = testResults.Where(x => !x.Result!.HasError).ToList();
            if (validResults.Count > 1)
            {
                var first = validResults.First().Result!;
                var last = validResults.Last().Result!;
                var totalImprovement = ((first.AverageTimeMs - last.AverageTimeMs) / first.AverageTimeMs) * 100;
                
                if (Math.Abs(totalImprovement) > 1)
                {
                    var trend = totalImprovement > 0 ? "improved" : "regressed";
                    Console.WriteLine($"  ► Overall trend: {trend} by {Math.Abs(totalImprovement):F1}%");
                }
            }
        }
    }
}