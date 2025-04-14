using System;
using System.Diagnostics;
using System.IO;

namespace Shelltrac
{

    class Program
    {
        static void Main(string[] args)
        {
            //string source = File.ReadAllText("disk_pressure.s");
            string source = File.ReadAllText("test3.s");

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var scanner = new DSLScanner(source);
                var tokens = scanner.ScanTokens();
                Console.WriteLine("finished scanning");

                var parser = new DSLParser(tokens, source);
                var programNode = parser.ParseProgram();
                Console.WriteLine("finished parsing");

                var executor = new Executor("foobarfixme.s", source);
                executor.Execute(programNode);
            }
        catch (ShelltracException ex)
{
  Console.WriteLine("yoloswag");
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n[ERROR] {ex.Message}");
    Console.ResetColor();
    
    // Show location information
    if (ex.Line > 0)
    {
        Console.WriteLine($"Location: line {ex.Line}" + 
            (ex.Column > 0 ? $", column {ex.Column}" : ""));
    }
    
    // Print the context fragment if available
    if (!string.IsNullOrEmpty(ex.ScriptFragment))
    {
        Console.WriteLine();
        Console.WriteLine(ex.ScriptFragment);
    }
    else { Console.WriteLine("debug no script fragment"); }
    
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

    }

    else { Console.WriteLine("debug no inner xeceptio"); }
}
            catch (Exception ex)
            {
                Console.WriteLine($"exception!! [Error] {ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                Console.WriteLine(
                    $"Execution terminated after {FormatTimeSpan(stopwatch.Elapsed)}."
                );
            }
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
