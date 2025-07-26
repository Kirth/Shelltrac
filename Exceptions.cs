using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Shelltrac
{
    /// <summary>
    /// Rich error context for Shelltrac debugging with call stack and source snippets
    /// </summary>
    public class ShelltracErrorContext
    {
        public string FileName { get; }
        public int Line { get; }
        public int Column { get; }
        public string SourceSnippet { get; }
        public Stack<string> CallStack { get; }
        public Dictionary<string, object?> Variables { get; }

        public ShelltracErrorContext(
            string fileName = "",
            int line = 0,
            int column = 0,
            string sourceSnippet = "",
            Stack<string>? callStack = null,
            Dictionary<string, object?>? variables = null)
        {
            FileName = fileName;
            Line = line;
            Column = column;
            SourceSnippet = sourceSnippet;
            CallStack = callStack ?? new Stack<string>();
            Variables = variables ?? new Dictionary<string, object?>();
        }

        public ShelltracException CreateException(string message, Exception? innerException = null)
        {
            return new ShelltracException(message, this, innerException);
        }

        public RuntimeException CreateRuntimeException(string message, Exception? innerException = null)
        {
            return new RuntimeException(message, this, innerException);
        }

        public ParsingException CreateParsingException(string message, Exception? innerException = null)
        {
            return new ParsingException(message, this, innerException);
        }

        /// <summary>
        /// Creates a new context with an additional call frame
        /// </summary>
        public ShelltracErrorContext PushCallFrame(string functionName)
        {
            var newCallStack = new Stack<string>(CallStack.Reverse());
            newCallStack.Push(functionName);
            return new ShelltracErrorContext(FileName, Line, Column, SourceSnippet, newCallStack, Variables);
        }

        /// <summary>
        /// Creates a context with current variable state for debugging
        /// </summary>
        public ShelltracErrorContext WithVariables(Dictionary<string, object?> variables)
        {
            return new ShelltracErrorContext(FileName, Line, Column, SourceSnippet, CallStack, variables);
        }
    }
    /// <summary>
    /// Base exception class for all Shelltrac-specific exceptions with rich context
    /// </summary>
    public class ShelltracException : Exception
    {
        public ShelltracErrorContext Context { get; }

        // Legacy properties for backward compatibility
        public int Line => Context.Line;
        public int Column => Context.Column;
        public string ScriptFragment => Context.SourceSnippet;

        public ShelltracException(
            string message,
            ShelltracErrorContext context,
            Exception? innerException = null
        )
            : base(message, innerException)
        {
            Context = context;
        }

        // Legacy constructor for backward compatibility
        public ShelltracException(
            string message,
            int line = 0,
            int column = 0,
            string scriptFragment = "",
            Exception? innerException = null
        )
            : base(message, innerException)
        {
            Context = new ShelltracErrorContext(
                fileName: "",
                line: line,
                column: column,
                sourceSnippet: scriptFragment
            );
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("╔═════════════════════════════════════════════════════════════════╗");
            sb.AppendLine($"║ ERROR: {Message.PadRight(55)} ║");
            sb.AppendLine("╠═════════════════════════════════════════════════════════════════╣");

            if (!string.IsNullOrEmpty(Context.FileName))
                sb.AppendLine($"║ File: {Context.FileName.PadRight(58)} ║");

            if (Context.Line > 0)
            {
                string location = $"Location: line {Context.Line}";
                if (Context.Column > 0)
                    location += $", column {Context.Column}";
                sb.AppendLine($"║ {location.PadRight(63)} ║");
            }

            sb.AppendLine("╚═════════════════════════════════════════════════════════════════╝");

            // Display source code context prominently
            if (!string.IsNullOrEmpty(Context.SourceSnippet))
                sb.Append(Context.SourceSnippet);

            if (Context.CallStack.Any())
            {
                sb.AppendLine();
                sb.AppendLine("┌─ Call Stack ──────────────────────────────────────────────────────┐");
                foreach (var frame in Context.CallStack)
                    sb.AppendLine($"│ at {frame.PadRight(60)} │");
                sb.AppendLine("└───────────────────────────────────────────────────────────────────┘");
            }

            if (Context.Variables.Any())
            {
                sb.AppendLine();
                sb.AppendLine("┌─ Variables ───────────────────────────────────────────────────────┐");
                foreach (var (name, value) in Context.Variables.Take(15)) // Show more variables
                {
                    string valueStr = value?.ToString() ?? "null";
                    if (valueStr.Length > 45) valueStr = valueStr.Substring(0, 45) + "...";
                    
                    // Format variable display nicely
                    string varLine = $"{name} = {valueStr}";
                    if (varLine.Length > 63) varLine = varLine.Substring(0, 60) + "...";
                    sb.AppendLine($"│ {varLine.PadRight(63)} │");
                }
                if (Context.Variables.Count > 15)
                    sb.AppendLine($"│ ... and {Context.Variables.Count - 15} more variables{new string(' ', 63 - $"... and {Context.Variables.Count - 15} more variables".Length)} │");
                sb.AppendLine("└───────────────────────────────────────────────────────────────────┘");
            }

            if (InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine($"Inner Exception: {InnerException.Message}");
            }

            return sb.ToString();
        }
    }

    public class ParsingException : ShelltracException
    {
        public ParsingException(
            string message,
            ShelltracErrorContext context,
            Exception? innerException = null
        )
            : base($"Parsing error: {message}", context, innerException) { }

        // Legacy constructor for backward compatibility
        public ParsingException(
            string message,
            int line = 0,
            int column = 0,
            string scriptFragment = "",
            Exception? innerException = null
        )
            : base($"Parsing error: {message}", line, column, scriptFragment, innerException) { }
    }

    public class RuntimeException : ShelltracException
    {
        public RuntimeException(
            string message,
            ShelltracErrorContext context,
            Exception? innerException = null
        )
            : base($"Runtime error: {message}", context, innerException) { }

        // Legacy constructor for backward compatibility
        public RuntimeException(
            string message,
            int line = 0,
            int column = 0,
            string scriptFragment = "",
            Exception? innerException = null
        )
            : base($"Runtime error: {message}", line, column, scriptFragment, innerException) { }
    }

    public class ShellCommandException : RuntimeException
    {
        public string Command { get; }
        public int? ExitCode { get; }

        public ShellCommandException(
            string message,
            string command,
            int? exitCode = null,
            int line = 0,
            int column = 0,
            string scriptFragment = "",
            Exception? innerException = null
        )
            : base($"Shell command failed: {message}", line, column, scriptFragment, innerException)
        {
            Command = command;
            ExitCode = exitCode;
        }

        public override string ToString()
        {
            string exitCodeInfo = ExitCode.HasValue ? $" (exit code: {ExitCode})" : "";
            return $"{base.ToString()}{exitCodeInfo}\nCommand: {Command}";
        }
    }

    public class SshCommandException : ShellCommandException
    {
        public string Host { get; }

        public SshCommandException(
            string message,
            string host,
            string command,
            int? exitCode = null,
            int line = 0,
            int column = 0,
            string scriptFragment = "",
            Exception? innerException = null
        )
            : base(
                $"SSH command failed: {message}",
                command,
                exitCode,
                line,
                column,
                scriptFragment,
                innerException
            )
        {
            Host = host;
        }

        public override string ToString()
        {
            return $"{base.ToString()}\nHost: {Host}";
        }
    }
}
