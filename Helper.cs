using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Shelltrac
{
    public static class Helper
    {
        public static string ToPrettyString(this object? obj)
        {
            if (obj is null)
                return "null";

            if (obj is System.Collections.IDictionary dict)
            {
                var sb = new StringBuilder();
                sb.Append("{ ");
                bool first = true;
                foreach (var key in dict.Keys)
                {
                    if (!first)
                        sb.Append(", ");
                    sb.Append($"{key.ToPrettyString()}: {dict[key].ToPrettyString()}");
                    first = false;
                }
                sb.Append(" }");
                return sb.ToString();
            }

            if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
            {
                var sb = new StringBuilder();
                sb.Append("[ ");
                bool first = true;
                foreach (var item in enumerable)
                {
                    if (!first)
                        sb.Append(", ");
                    sb.Append(item.ToPrettyString());
                    first = false;
                }
                sb.Append(" ]");
                return sb.ToString();
            }

            return obj.ToString()!;
        }

        public static string ExecuteSsh(string host, string command)
        {
            try
            {
                var psi = new ProcessStartInfo("ssh")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add(host);
                psi.ArgumentList.Add(command);

                //Console.WriteLine("Executing SSH");
                //Console.WriteLine(command);
                using var proc = Process.Start(psi);
                proc.WaitForExit();
                string output = proc.StandardOutput.ReadToEnd();
                string err = proc.StandardError.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(err))
                    output += "\n[SSH ERROR] " + err;
                return output;
            }
            catch (Exception e)
            {
                throw new Exception("[SSH Exception] " + e.Message);
            }
        }

        public static string AppendNewLine(this string input)
        { // embarassingly needed for debugging
            return input + "\n";
        }

        public static string[] Lines(this string input)
        {
            return input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }
    }

    public class ReflectionCallable : Callable
    {
        private readonly object _target;
        private readonly List<MethodInfo> _methods;

        public ReflectionCallable(object target, List<MethodInfo> methods)
        {
            _target = target;
            _methods = methods;
        }

        public object? Call(Executor executor, List<object?> arguments)
        {
            // Try to select a method based on the number of arguments
            MethodInfo? method = _methods.FirstOrDefault(m =>
                m.GetParameters().Length == arguments.Count
            );
            if (method == null)
            {
                throw new Exception(
                    $"No method found that matches argument count: {arguments.Count}"
                );
            }

            // Convert DSL arguments to parameter types as needed
            ParameterInfo[] parameters = method.GetParameters();
            object?[] convertedArgs = new object?[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
            {
                convertedArgs[i] = Convert.ChangeType(arguments[i], parameters[i].ParameterType);
            }

            // Invoke the method on the target object
            object? result = method.Invoke(_target, convertedArgs);

            // If the result is already a collection that represents multiple values, return it directly
            if (result is IEnumerable<object> objCollection && !(result is string))
            {
                return objCollection.ToList();
            }

            // Otherwise just return the single value
            return result;
        }
    }
}
