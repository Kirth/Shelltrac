using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Shelltrac
{
    /// <summary>
    /// Interactive REPL (Read-Eval-Print-Loop) for Shelltrac
    /// </summary>
    public class ShelltracREPL
    {
        private readonly Executor _executor;
        private readonly bool _debugMode;
        private readonly Dictionary<string, Action<string>> _specialCommands;
        private readonly string _historyFile;
        private readonly List<string> _history = new List<string>();
        private int _historyIndex = 0;

        public ShelltracREPL(Executor executor, bool debugMode)
        {
            _executor = executor ?? new Executor("repl", "");
            _debugMode = debugMode;

            // Set up history file in user's home directory
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Shelltrac"
            );

            Directory.CreateDirectory(appDataDir);
            _historyFile = Path.Combine(appDataDir, "repl_history.txt");

            // Try to load history
            LoadHistory();

            // Define special commands
            _specialCommands = new Dictionary<string, Action<string>>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                { "load", LoadFile },
                { "vars", _ => DisplayVariables() },
                { "clear", _ => Console.Clear() },
                { "history", _ => DisplayHistory() },
                { "save", SaveToFile },
                { "help", _ => DisplayHelp() },
            };
        }

        /// <summary>
        /// Start the REPL
        /// </summary>
        public void Start()
        {
            Console.WriteLine("Shelltrac Interactive REPL");
            Console.WriteLine("Type '.help' for available commands, 'exit' to quit");

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("shelltrac> ");
                Console.ResetColor();

                string input = ReadInputWithHistory()?.Trim()!;

                if (string.IsNullOrEmpty(input))
                    continue;

                // Check for exit command
                if (
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase)
                    || input.Equals("quit", StringComparison.OrdinalIgnoreCase)
                )
                {
                    break;
                }

                // Add to history if not empty and not a duplicate of the last entry
                if (
                    !string.IsNullOrWhiteSpace(input)
                    && (_history.Count == 0 || input != _history[_history.Count - 1])
                )
                {
                    _history.Add(input);
                    _historyIndex = _history.Count;

                    // Save history periodically
                    if (_history.Count % 5 == 0)
                        SaveHistory();
                }

                try
                {
                    // Check for special commands
                    if (input.StartsWith("."))
                    {
                        ProcessSpecialCommand(input.Substring(1));
                    }
                    else
                    {
                        // Execute regular code
                        ExecuteInput(input);
                    }
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                }
            }

            // Save history on exit
            SaveHistory();
            Console.WriteLine("Goodbye REPL!");
        }

        /// <summary>
        /// Execute Shelltrac code input
        /// </summary>
        private void ExecuteInput(string input)
        {
            try
            {
                if (_debugMode)
                    Console.WriteLine("DEBUG: Scanning input");

                var scanner = new DSLScanner(input);
                var tokens = scanner.ScanTokens();

                if (_debugMode)
                    Console.WriteLine($"DEBUG: Scanned {tokens.Count} tokens");

                var parser = new DSLParser(tokens, input);
                var programNode = parser.ParseProgram();

                if (_debugMode)
                    Console.WriteLine("DEBUG: Parsed input successfully");

                var stopwatch = Stopwatch.StartNew();
                _executor.Execute(programNode);
                stopwatch.Stop();

                if (_debugMode)
                    Console.WriteLine($"DEBUG: Execution time: {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (ShelltracException ex)
            {
                HandleShelltracException(ex);
            }
        }

        /// <summary>
        /// Process a special REPL command
        /// </summary>
        private void ProcessSpecialCommand(string command)
        {
            // Split the command into parts
            string[] parts = command
                .Trim()
                .Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            string args = parts.Length > 1 ? parts[1] : "";

            if (_specialCommands.TryGetValue(cmd, out var action))
            {
                action(args);
            }
            else
            {
                Console.WriteLine($"Unknown command: .{cmd}");
                DisplayHelp();
            }
        }

        /// <summary>
        /// Load and execute a script file
        /// </summary>
        private void LoadFile(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                Console.WriteLine("Usage: .load <filename>");
                return;
            }

            try
            {
                if (!File.Exists(filename))
                {
                    Console.WriteLine($"File not found: {filename}");
                    return;
                }

                string source = File.ReadAllText(filename);
                Console.WriteLine($"Loaded {filename}, {source.Length} characters");

                // Execute the loaded script
                ExecuteInput(source);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error loading file: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Save current REPL code to a file
        /// </summary>
        private void SaveToFile(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                Console.WriteLine("Usage: .save <filename>");
                return;
            }

            try
            {
                // Create a script from the last few history entries
                var scriptBuilder = new System.Text.StringBuilder();

                // Get the last 10 entries or fewer that aren't special commands
                int count = 0;
                for (int i = _history.Count - 1; i >= 0 && count < 10; i--)
                {
                    if (
                        !_history[i].StartsWith(".")
                        && !_history[i].Equals("exit")
                        && !_history[i].Equals("quit")
                    )
                    {
                        scriptBuilder.Insert(0, _history[i] + Environment.NewLine);
                        count++;
                    }
                }

                if (scriptBuilder.Length == 0)
                {
                    Console.WriteLine("No code to save. Enter some code first.");
                    return;
                }

                File.WriteAllText(filename, scriptBuilder.ToString());
                Console.WriteLine($"Saved to {filename}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error saving file: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Display current variables
        /// </summary>
        private void DisplayVariables()
        {
            var variables = _executor.Context.GlobalScope.Variables;

            if (variables.Count == 0)
            {
                Console.WriteLine("No variables defined.");
                return;
            }

            Console.WriteLine("Current variables:");
            foreach (var kvp in variables)
            {
                // Skip builtin functions and internal variables
                if (kvp.Value is TypedBuiltinFunction || kvp.Key.StartsWith("_"))
                    continue;

                Console.WriteLine($"{kvp.Key} = {Helper.ToPrettyString(kvp.Value)}");
            }
        }

        /// <summary>
        /// Display command history
        /// </summary>
        private void DisplayHistory()
        {
            if (_history.Count == 0)
            {
                Console.WriteLine("No history yet.");
                return;
            }

            Console.WriteLine("Command history:");
            int startIndex = Math.Max(0, _history.Count - 20); // Show last 20 entries

            for (int i = startIndex; i < _history.Count; i++)
            {
                Console.WriteLine($"{i + 1,3}: {_history[i]}");
            }
        }

        /// <summary>
        /// Display help information
        /// </summary>
        private void DisplayHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("  .load <filename>  - Load and execute a script file");
            Console.WriteLine("  .save <filename>  - Save recent commands to a script file");
            Console.WriteLine("  .vars             - Display current variables");
            Console.WriteLine("  .history          - Display command history");
            Console.WriteLine("  .clear            - Clear the console");
            Console.WriteLine("  .help             - Display this help message");
            Console.WriteLine("  exit, quit        - Exit the REPL");
            Console.WriteLine();
            Console.WriteLine("Navigation:");
            Console.WriteLine("  Up/Down arrows    - Navigate command history");
            Console.WriteLine();
            Console.WriteLine("Type Shelltrac code directly to execute it.");
        }

        /// <summary>
        /// Handle Shelltrac-specific exceptions
        /// </summary>
        private void HandleShelltracException(ShelltracException ex)
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

            // Print inner exception details if available
            if (ex.InnerException != null)
            {
                Console.WriteLine("\nCaused by:");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(ex.InnerException.Message);
                Console.ResetColor();

                if (_debugMode)
                {
                    Console.WriteLine("\nInner Exception Stack Trace:");
                    Console.WriteLine(ex.InnerException.StackTrace);
                }
            }

            if (_debugMode)
            {
                Console.WriteLine("\nException Stack Trace:");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Handle general errors
        /// </summary>
        private void HandleError(Exception ex)
        {
            if (ex is ShelltracException shelltracEx)
            {
                HandleShelltracException(shelltracEx);
                return;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();

            if (_debugMode)
            {
                Console.WriteLine("\nStack Trace:");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Read input with history navigation (up/down arrows)
        /// </summary>
        private string ReadInputWithHistory()
        {
            var input = new System.Text.StringBuilder();
            int cursorPos = 0;

            while (true)
            {
                var key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        Console.WriteLine(); // Move to next line
                        return input.ToString();

                    case ConsoleKey.Backspace:
                        if (cursorPos > 0)
                        {
                            input.Remove(cursorPos - 1, 1);
                            cursorPos--;
                            RefreshInputLine(input.ToString(), cursorPos);
                        }
                        break;

                    case ConsoleKey.Delete:
                        if (cursorPos < input.Length)
                        {
                            input.Remove(cursorPos, 1);
                            RefreshInputLine(input.ToString(), cursorPos);
                        }
                        break;

                    case ConsoleKey.LeftArrow:
                        if (cursorPos > 0)
                        {
                            cursorPos--;
                            Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                        }
                        break;

                    case ConsoleKey.RightArrow:
                        if (cursorPos < input.Length)
                        {
                            cursorPos++;
                            Console.SetCursorPosition(Console.CursorLeft + 1, Console.CursorTop);
                        }
                        break;

                    case ConsoleKey.UpArrow:
                        if (_history.Count > 0 && _historyIndex > 0)
                        {
                            _historyIndex--;
                            input.Clear();
                            input.Append(_history[_historyIndex]);
                            cursorPos = input.Length;
                            RefreshInputLine(input.ToString(), cursorPos);
                        }
                        break;

                    case ConsoleKey.DownArrow:
                        if (_historyIndex < _history.Count - 1)
                        {
                            _historyIndex++;
                            input.Clear();
                            input.Append(_history[_historyIndex]);
                            cursorPos = input.Length;
                            RefreshInputLine(input.ToString(), cursorPos);
                        }
                        else if (_historyIndex == _history.Count - 1)
                        {
                            // At the end of history, show empty line
                            _historyIndex = _history.Count;
                            input.Clear();
                            cursorPos = 0;
                            RefreshInputLine("", 0);
                        }
                        break;

                    case ConsoleKey.Home:
                        cursorPos = 0;
                        Console.SetCursorPosition("shelltrac> ".Length, Console.CursorTop);
                        break;

                    case ConsoleKey.End:
                        cursorPos = input.Length;
                        Console.SetCursorPosition(
                            "shelltrac> ".Length + input.Length,
                            Console.CursorTop
                        );
                        break;

                    default:
                        if (key.KeyChar >= 32 && key.KeyChar <= 126) // Printable characters
                        {
                            input.Insert(cursorPos, key.KeyChar);
                            cursorPos++;
                            RefreshInputLine(input.ToString(), cursorPos);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Refresh the input line with the current input text
        /// </summary>

        private void RefreshInputLine(string text, int cursorPos)
        {
            // Store current console properties to avoid multiple property accesses
            int consoleWidth = Console.WindowWidth;
            int currentLine = Console.CursorTop;
            string prompt = "shelltrac> ";
            int promptLength = prompt.Length;

            // Calculate available width for text
            int maxTextLength = consoleWidth - promptLength;

            // Clear the entire current line first
            Console.SetCursorPosition(0, currentLine);
            Console.Write(new string(' ', consoleWidth));

            // Reset cursor to beginning of line
            Console.SetCursorPosition(0, currentLine);

            // Write the prompt
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(prompt);
            Console.ResetColor();

            // Handle text that is longer than available width
            string displayText = text;
            int adjustedCursorPos = cursorPos;

            if (text.Length > maxTextLength)
            {
                // Determine visible portion based on cursor position
                int startIndex = 0;

                // If cursor is beyond visible area, adjust starting point
                if (cursorPos >= maxTextLength)
                {
                    // Show text centered around cursor with preference to showing text after cursor
                    int textAfterCursor = Math.Min(text.Length - cursorPos, maxTextLength / 3);
                    int textBeforeCursor = maxTextLength - textAfterCursor - 1; // -1 for ellipsis
                    startIndex = Math.Max(0, cursorPos - textBeforeCursor);

                    displayText =
                        (startIndex > 0 ? "…" : "")
                        + text.Substring(
                            startIndex,
                            Math.Min(
                                text.Length - startIndex,
                                maxTextLength - (startIndex > 0 ? 1 : 0)
                            )
                        );

                    // Adjust cursor position accounting for truncation and ellipsis
                    adjustedCursorPos = cursorPos - startIndex + (startIndex > 0 ? 1 : 0);
                }
                else
                {
                    // If cursor is in visible area, show as much as possible from the start
                    displayText = text.Substring(0, maxTextLength - 1) + "…";
                    // No need to adjust cursor position as it's already in visible area
                }
            }

            // Make sure we don't exceed console width
            if (displayText.Length > maxTextLength)
                displayText = displayText.Substring(0, maxTextLength);

            // Write the input text
            Console.Write(displayText);

            // Position the cursor - make sure it's within bounds
            adjustedCursorPos = Math.Min(adjustedCursorPos, maxTextLength - 1);
            adjustedCursorPos = Math.Max(adjustedCursorPos, 0);
            Console.SetCursorPosition(promptLength + adjustedCursorPos, currentLine);
        }

        /// <summary>
        /// Load command history from file
        /// </summary>
        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFile))
                {
                    string[] lines = File.ReadAllLines(_historyFile);
                    _history.AddRange(lines);
                    _historyIndex = _history.Count;
                }
            }
            catch (Exception)
            {
                // Silently fail if history can't be loaded
            }
        }

        /// <summary>
        /// Save command history to file
        /// </summary>
        private void SaveHistory()
        {
            try
            {
                // Keep only the last 100 commands
                var historyToSave =
                    _history.Count <= 100 ? _history : _history.GetRange(_history.Count - 100, 100);

                File.WriteAllLines(_historyFile, historyToSave);
            }
            catch (Exception)
            {
                // Silently fail if history can't be saved
            }
        }
    }
}
