using System;

namespace Shelltrac
{
    /// <summary>
    /// Base exception class for all Shelltrac-specific exceptions
    /// </summary>
    public class ShelltracException : Exception
    {
        /// <summary>
        /// Line number where the error occurred
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// Column number where the error occurred
        /// </summary>
        public int Column { get; }

        /// <summary>
        /// The script fragment where the error occurred (for context)
        /// </summary>
        public string ScriptFragment { get; }

        public ShelltracException(
            string message,
            int line = 0,
            int column = 0,
            string scriptFragment = "",
            Exception? innerException = null
        )
            : base(message, innerException)
        {
            Line = line;
            Column = column;
            ScriptFragment = scriptFragment;
        }

        public override string ToString()
        {
            string location =
                Line > 0 ? $" at line {Line}" + (Column > 0 ? $", column {Column}" : "") : "";
            return $"Shelltrac error{location}: {Message}";
        }
    }

    /// <summary>
    /// Exception thrown during parsing of Shelltrac scripts
    /// </summary>
    public class ParsingException : ShelltracException
    {
        public ParsingException(
            string message,
            int line = 0,
            int column = 0,
            string scriptFragment = "",
            Exception? innerException = null
        )
            : base($"Parsing error: {message}", line, column, scriptFragment, innerException) { }
    }

    /// <summary>
    /// Exception thrown during execution of Shelltrac scripts
    /// </summary>
    public class RuntimeException : ShelltracException
    {
        public RuntimeException(
            string message,
            int line = 0,
            int column = 0,
            string scriptFragment = "",
            Exception? innerException = null
        )
            : base($"Runtime error: {message}", line, column, scriptFragment, innerException) { }
    }

    /// <summary>
    /// Exception thrown when a shell command fails
    /// </summary>
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

    /// <summary>
    /// Exception thrown when an SSH command fails
    /// </summary>
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
