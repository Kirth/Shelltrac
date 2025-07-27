using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Shelltrac
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // Check for benchmark command
            if (args.Length > 0 && args[0] == "benchmark")
            {
                var benchmarkArgs = args.Length > 1 ? args[1..] : new string[0];
                Shelltrac.Benchmarks.PerformanceTestRunner.MainBenchmarks(benchmarkArgs);
                return 0;
            }

            var scriptArgument = new Argument<FileInfo>(
                "script",
                description: "Script file to execute",
                getDefaultValue: () => null!
            );

            var debugOption = new Option<bool>("--debug", "Enable debug mode with verbose output");

            var replOption = new Option<bool>(
                "--repl",
                "Enter REPL mode after executing the script (or immediately if no script provided)"
            );

            var rootCommand = new RootCommand("Shelltrac script interpreter")
            {
                scriptArgument,
                debugOption,
                replOption,
            };

            rootCommand.SetHandler(
                (FileInfo script, bool debug, bool repl) =>
                {
                    ExecuteWithOptions(script, debug, repl);
                },
                scriptArgument,
                debugOption,
                replOption
            );

            return await rootCommand.InvokeAsync(args);
        }

        private static void ExecuteWithOptions(FileInfo scriptFile, bool debugMode, bool startRepl)
        {
            var stopwatch = Stopwatch.StartNew();
            Executor executor = null;

            try
            {
                // If script provided, run it
                if (scriptFile != null)
                {
                    if (!scriptFile.Exists)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(
                            $"Error: Script file '{scriptFile.FullName}' does not exist."
                        );
                        Console.ResetColor();
                        return;
                    }

                    string scriptPath = scriptFile.FullName;
                    string source = File.ReadAllText(scriptPath);

                    if (debugMode)
                    {
                        Console.WriteLine($"DEBUG: Loading script from {scriptPath}");
                        Console.WriteLine(
                            $"DEBUG: Script content length: {source.Length} characters"
                        );
                    }

                    try
                    {
                        var phaseStopwatch = Stopwatch.StartNew();
                        
                        if (debugMode)
                            Console.WriteLine("DEBUG: Starting scanning phase");

                        var scanner = new DSLScanner(source);
                        var tokens = scanner.ScanTokens();
                        
                        phaseStopwatch.Stop();
                        if (debugMode)
                            Console.WriteLine(
                                $"DEBUG: Scanning complete, found {tokens.Count} tokens in {phaseStopwatch.ElapsedMilliseconds}ms"
                            );

                        phaseStopwatch.Restart();
                        if (debugMode)
                            Console.WriteLine("DEBUG: Starting parsing phase");

                        var parser = new DSLParser(tokens, source);
                        var programNode = parser.ParseProgram();

                        phaseStopwatch.Stop();
                        if (debugMode)
                            Console.WriteLine($"DEBUG: Parsing complete in {phaseStopwatch.ElapsedMilliseconds}ms");

                        phaseStopwatch.Restart();
                        if (debugMode)
                            Console.WriteLine("DEBUG: Starting execution phase");

                        executor = new Executor(scriptPath, source);
                        executor.Execute(programNode);

                        phaseStopwatch.Stop();
                        if (debugMode)
                            Console.WriteLine($"DEBUG: Execution complete in {phaseStopwatch.ElapsedMilliseconds}ms");
                    }
                    catch (ShelltracException ex)
                    {
                        HandleException(ex, debugMode);
                        // Don't enter REPL mode if the script had errors
                        if (startRepl)
                        {
                            Console.WriteLine(
                                "\nScript had errors, REPL will be started in a dodgy environment."
                            );
                        }
                        else
                        {
                            return;
                        }
                    }
                }

                // If REPL mode requested, start it (either after script or directly)
                if (startRepl)
                {
                    if (scriptFile != null && executor != null)
                    {
                        Console.WriteLine(
                            "\n================\nScript execution complete. Entering REPL mode with script context..."
                        );
                    }
                    else
                    {
                        Console.WriteLine("Starting REPL mode...");
                    }

                    var repl = new ShelltracREPL(executor, debugMode);
                    repl.Start();
                }
                else if (scriptFile == null)
                {
                    // No script and no REPL - print usage
                    Console.WriteLine("Error: No script file provided and --repl flag not set.");
                    Console.WriteLine("\nUsage: dotnet run [script.s] [--debug] [--repl]");
                    Console.WriteLine("  --debug     Enable debug mode with verbose output");
                    Console.WriteLine(
                        "  --repl      Enter REPL mode after executing the script (or immediately if no script provided)"
                    );
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Unexpected error: {ex.Message}");
                Console.ResetColor();

                if (debugMode)
                {
                    Console.WriteLine("\nStack Trace:");
                    Console.WriteLine(ex.StackTrace);
                }
            }
            finally
            {
                stopwatch.Stop();
                Console.WriteLine(
                    $"Execution terminated after {FormatTimeSpan(stopwatch.Elapsed)}."
                );
            }
        }

        private static void HandleException(ShelltracException ex, bool debugMode)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR] {ex.Message}");
            Console.ResetColor();

            // Show location information
            if (ex.Line > 0)
            {
                Console.WriteLine(
                    $"Location: line {ex.Line}" + (ex.Column > 0 ? $", column {ex.Column}" : "")
                );
            }

            // Print the context fragment if available
            if (!string.IsNullOrEmpty(ex.ScriptFragment))
            {
                Console.WriteLine();
                Console.WriteLine(ex.ScriptFragment);
            }

            // Show call stack for nested function calls
            if (ex is RuntimeException)
            {
                Console.WriteLine("\nError occurred during script execution.");
            }

            // Print inner exception details if available
            if (ex.InnerException != null)
            {
                Console.WriteLine("\nCaused by:");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(ex.InnerException.Message);
                Console.ResetColor();

                Console.WriteLine("\nInner Exception Stack Trace:");
                Console.WriteLine(ex.InnerException.StackTrace);
            }

            Console.WriteLine("\nException Stack Trace:");
            Console.WriteLine(ex.StackTrace);
        }

        static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalMilliseconds < 1000)
                return $"{ts.TotalMilliseconds:0}ms";

            if (ts.TotalSeconds < 60)
                return $"{ts.TotalSeconds:0}s";

            if (ts.TotalMinutes < 60)
                return $"{ts.Minutes}m {ts.Seconds}s";

            if (ts.TotalHours < 24)
                return $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";

            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
        }
    }
}
