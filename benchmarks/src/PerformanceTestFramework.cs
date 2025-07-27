using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Shelltrac.Benchmarks
{
    /// <summary>
    /// Simple performance testing framework for measuring Shelltrac operations
    /// </summary>
    public class PerformanceTestFramework
    {
        public class TestResult
        {
            public string TestName { get; set; } = "";
            public TimeSpan AverageTime { get; set; }
            public TimeSpan MinTime { get; set; }
            public TimeSpan MaxTime { get; set; }
            public long AverageMemory { get; set; }
            public long PeakMemory { get; set; }
            public int Iterations { get; set; }
            public Exception? Error { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public string GitCommit { get; set; } = "";
            public string TestCategory { get; set; } = "";
            
            public override string ToString()
            {
                if (Error != null)
                    return $"{TestName}: ERROR - {Error.Message}";
                    
                return $"{TestName}: Avg={AverageTime.TotalMilliseconds:F2}ms, " +
                       $"Min={MinTime.TotalMilliseconds:F2}ms, " +
                       $"Max={MaxTime.TotalMilliseconds:F2}ms, " +
                       $"Memory={AverageMemory / 1024:N0}KB, " +
                       $"Peak={PeakMemory / 1024:N0}KB, " +
                       $"Iterations={Iterations}";
            }
        }

        /// <summary>
        /// Run a performance test with the given action
        /// </summary>
        public static TestResult RunTest(string testName, Action testAction, int iterations = 100, int warmupIterations = 10)
        {
            var result = new TestResult
            {
                TestName = testName,
                Iterations = iterations
            };

            try
            {
                // Warmup runs
                for (int i = 0; i < warmupIterations; i++)
                {
                    testAction();
                }

                // Force GC before measurement
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var times = new List<TimeSpan>();
                var memoryUsages = new List<long>();
                long peakMemory = 0;

                for (int i = 0; i < iterations; i++)
                {
                    var initialMemory = GC.GetTotalMemory(false);
                    var sw = Stopwatch.StartNew();
                    
                    testAction();
                    
                    sw.Stop();
                    var finalMemory = GC.GetTotalMemory(false);
                    var memoryUsed = Math.Max(0, finalMemory - initialMemory);
                    
                    times.Add(sw.Elapsed);
                    memoryUsages.Add(memoryUsed);
                    peakMemory = Math.Max(peakMemory, finalMemory);
                }

                result.AverageTime = TimeSpan.FromTicks((long)times.Average(t => t.Ticks));
                result.MinTime = times.Min();
                result.MaxTime = times.Max();
                result.AverageMemory = (long)memoryUsages.Average();
                result.PeakMemory = peakMemory;
            }
            catch (Exception ex)
            {
                result.Error = ex;
            }

            return result;
        }

        /// <summary>
        /// Run an async performance test
        /// </summary>
        public static async Task<TestResult> RunTestAsync(string testName, Func<Task> testAction, int iterations = 100, int warmupIterations = 10)
        {
            var result = new TestResult
            {
                TestName = testName,
                Iterations = iterations
            };

            try
            {
                // Warmup runs
                for (int i = 0; i < warmupIterations; i++)
                {
                    await testAction();
                }

                // Force GC before measurement
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var times = new List<TimeSpan>();
                var memoryUsages = new List<long>();
                long peakMemory = 0;

                for (int i = 0; i < iterations; i++)
                {
                    var initialMemory = GC.GetTotalMemory(false);
                    var sw = Stopwatch.StartNew();
                    
                    await testAction();
                    
                    sw.Stop();
                    var finalMemory = GC.GetTotalMemory(false);
                    var memoryUsed = Math.Max(0, finalMemory - initialMemory);
                    
                    times.Add(sw.Elapsed);
                    memoryUsages.Add(memoryUsed);
                    peakMemory = Math.Max(peakMemory, finalMemory);
                }

                result.AverageTime = TimeSpan.FromTicks((long)times.Average(t => t.Ticks));
                result.MinTime = times.Min();
                result.MaxTime = times.Max();
                result.AverageMemory = (long)memoryUsages.Average();
                result.PeakMemory = peakMemory;
            }
            catch (Exception ex)
            {
                result.Error = ex;
            }

            return result;
        }

        /// <summary>
        /// Compare two test results and show the performance difference
        /// </summary>
        public static void CompareResults(TestResult baseline, TestResult optimized)
        {
            Console.WriteLine($"\n=== Performance Comparison: {baseline.TestName} ===");
            Console.WriteLine($"Baseline:  {baseline}");
            Console.WriteLine($"Optimized: {optimized}");
            
            if (baseline.Error != null || optimized.Error != null)
            {
                Console.WriteLine("Cannot compare due to errors");
                return;
            }

            var timeImprovement = (baseline.AverageTime.TotalMilliseconds - optimized.AverageTime.TotalMilliseconds) / baseline.AverageTime.TotalMilliseconds * 100;
            var memoryImprovement = (baseline.AverageMemory - optimized.AverageMemory) / (double)baseline.AverageMemory * 100;

            Console.WriteLine($"Time Improvement:   {timeImprovement:F1}% ({(timeImprovement > 0 ? "faster" : "slower")})");
            Console.WriteLine($"Memory Improvement: {memoryImprovement:F1}% ({(memoryImprovement > 0 ? "less" : "more")} memory)");
        }

        /// <summary>
        /// Save test results to JSON file
        /// </summary>
        public static void SaveResults(List<TestResult> results, string fileName = "")
        {
            if (string.IsNullOrEmpty(fileName))
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                fileName = Path.Combine("benchmarks", $"benchmark_results_{timestamp}.json");
            }

            // Manual JSON serialization to avoid reflection issues
            var json = "[\n";
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                json += "  {\n";
                json += $"    \"TestName\": \"{result.TestName}\",\n";
                json += $"    \"AverageTimeMs\": {result.AverageTime.TotalMilliseconds:F4},\n";
                json += $"    \"MinTimeMs\": {result.MinTime.TotalMilliseconds:F4},\n";
                json += $"    \"MaxTimeMs\": {result.MaxTime.TotalMilliseconds:F4},\n";
                json += $"    \"AverageMemoryBytes\": {result.AverageMemory},\n";
                json += $"    \"PeakMemoryBytes\": {result.PeakMemory},\n";
                json += $"    \"Iterations\": {result.Iterations},\n";
                json += $"    \"HasError\": {(result.Error != null ? "true" : "false")},\n";
                json += $"    \"ErrorMessage\": \"{(result.Error?.Message ?? "").Replace("\"", "\\\"")}\",\n";
                json += $"    \"Timestamp\": \"{result.Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}\",\n";
                json += $"    \"GitCommit\": \"{result.GitCommit}\",\n";
                json += $"    \"TestCategory\": \"{result.TestCategory}\"\n";
                json += "  }";
                if (i < results.Count - 1) json += ",";
                json += "\n";
            }
            json += "]";

            File.WriteAllText(fileName, json);
            Console.WriteLine($"Results saved to: {fileName}");
        }

        /// <summary>
        /// Load test results from JSON file (simplified parsing)
        /// </summary>
        public static List<TestResult> LoadResults(string fileName)
        {
            if (!File.Exists(fileName))
                throw new FileNotFoundException($"Benchmark file not found: {fileName}");

            var results = new List<TestResult>();
            var json = File.ReadAllText(fileName);
            
            // Simple JSON parsing for our known format
            var lines = json.Split('\n');
            TestResult currentResult = null;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("{"))
                {
                    currentResult = new TestResult();
                }
                else if (trimmed.StartsWith("\"TestName\":") && currentResult != null)
                {
                    var value = ExtractJsonStringValue(trimmed);
                    currentResult.TestName = value;
                }
                else if (trimmed.StartsWith("\"AverageTimeMs\":") && currentResult != null)
                {
                    var value = ExtractJsonNumberValue(trimmed);
                    currentResult.AverageTime = TimeSpan.FromMilliseconds(value);
                }
                else if (trimmed.StartsWith("\"MinTimeMs\":") && currentResult != null)
                {
                    var value = ExtractJsonNumberValue(trimmed);
                    currentResult.MinTime = TimeSpan.FromMilliseconds(value);
                }
                else if (trimmed.StartsWith("\"MaxTimeMs\":") && currentResult != null)
                {
                    var value = ExtractJsonNumberValue(trimmed);
                    currentResult.MaxTime = TimeSpan.FromMilliseconds(value);
                }
                else if (trimmed.StartsWith("\"AverageMemoryBytes\":") && currentResult != null)
                {
                    var value = ExtractJsonNumberValue(trimmed);
                    currentResult.AverageMemory = (long)value;
                }
                else if (trimmed.StartsWith("\"PeakMemoryBytes\":") && currentResult != null)
                {
                    var value = ExtractJsonNumberValue(trimmed);
                    currentResult.PeakMemory = (long)value;
                }
                else if (trimmed.StartsWith("\"Iterations\":") && currentResult != null)
                {
                    var value = ExtractJsonNumberValue(trimmed);
                    currentResult.Iterations = (int)value;
                }
                else if (trimmed.StartsWith("\"GitCommit\":") && currentResult != null)
                {
                    var value = ExtractJsonStringValue(trimmed);
                    currentResult.GitCommit = value;
                }
                else if (trimmed.StartsWith("}") && currentResult != null)
                {
                    results.Add(currentResult);
                    currentResult = null;
                }
            }
            
            return results;
        }

        private static string ExtractJsonStringValue(string jsonLine)
        {
            var start = jsonLine.IndexOf("\"", jsonLine.IndexOf(":") + 1) + 1;
            var end = jsonLine.LastIndexOf("\"") - 1;
            return jsonLine.Substring(start, end - start + 1);
        }

        private static double ExtractJsonNumberValue(string jsonLine)
        {
            var start = jsonLine.IndexOf(":") + 1;
            var end = jsonLine.Length;
            if (jsonLine.EndsWith(",")) end--;
            var numberStr = jsonLine.Substring(start, end - start).Trim();
            return double.Parse(numberStr);
        }

        /// <summary>
        /// Compare current results with previous benchmark file
        /// </summary>
        public static void CompareWithBaseline(List<TestResult> currentResults, string baselineFile)
        {
            if (!File.Exists(baselineFile))
            {
                Console.WriteLine($"Baseline file not found: {baselineFile}");
                Console.WriteLine("Current results will be saved as new baseline.");
                return;
            }

            var baselineResults = LoadResults(baselineFile);
            
            Console.WriteLine("\n" + new string('=', 80));
            Console.WriteLine("PERFORMANCE COMPARISON WITH BASELINE");
            Console.WriteLine(new string('=', 80));

            foreach (var current in currentResults)
            {
                var baseline = baselineResults.FirstOrDefault(b => b.TestName == current.TestName);
                if (baseline != null && baseline.Error == null && current.Error == null)
                {
                    Console.WriteLine($"\n📊 {current.TestName}:");
                    
                    var timeDiff = (baseline.AverageTime.TotalMilliseconds - current.AverageTime.TotalMilliseconds) / baseline.AverageTime.TotalMilliseconds * 100;
                    var memoryDiff = (baseline.AverageMemory - current.AverageMemory) / (double)baseline.AverageMemory * 100;
                    
                    var timeIcon = timeDiff > 5 ? "🚀" : timeDiff < -5 ? "🐌" : "✅";
                    var memoryIcon = memoryDiff > 5 ? "💚" : memoryDiff < -5 ? "🔴" : "✅";
                    
                    Console.WriteLine($"  {timeIcon} Time:   {current.AverageTime.TotalMilliseconds:F2}ms vs {baseline.AverageTime.TotalMilliseconds:F2}ms ({timeDiff:+0.1;-0.1}%)");
                    Console.WriteLine($"  {memoryIcon} Memory: {current.AverageMemory/1024:N0}KB vs {baseline.AverageMemory/1024:N0}KB ({memoryDiff:+0.1;-0.1}%)");
                }
                else if (baseline == null)
                {
                    Console.WriteLine($"\n🆕 {current.TestName}: New test (no baseline)");
                }
            }
        }

        /// <summary>
        /// Get current git commit hash for tagging results
        /// </summary>
        public static string GetGitCommit()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "rev-parse --short HEAD",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var result = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return result;
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Run a suite of tests and generate a report
        /// </summary>
        public static void RunTestSuite(string suiteName, params (string name, Action test)[] tests)
        {
            Console.WriteLine($"\n=== {suiteName} Performance Test Suite ===");
            Console.WriteLine($"Running {tests.Length} tests...\n");

            var results = new List<TestResult>();
            
            foreach (var (name, test) in tests)
            {
                Console.Write($"Running {name}... ");
                var result = RunTest(name, test);
                results.Add(result);
                
                if (result.Error != null)
                    Console.WriteLine($"ERROR: {result.Error.Message}");
                else
                    Console.WriteLine($"{result.AverageTime.TotalMilliseconds:F2}ms avg");
            }

            Console.WriteLine("\n=== Results Summary ===");
            foreach (var result in results)
            {
                Console.WriteLine(result.ToString());
            }
        }
    }

}