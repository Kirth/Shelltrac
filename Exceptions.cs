using System;

namespace Shelltrac
{
    /// <summary>
    /// Base exception class for all Shelltrac-specific exceptions
    /// </summary>
    public class ShelltracException : Exception
    {
        public int Line { get; }

        public int Column { get; }

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
