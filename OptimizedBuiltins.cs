using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Shelltrac
{
    /// <summary>
    /// Base class for strongly-typed built-in functions with compile-time type checking
    /// </summary>
    public abstract class TypedBuiltinFunction : Callable
    {
        public string Name { get; }
        public abstract Type[] ParameterTypes { get; }
        public abstract Type ReturnType { get; }

        protected TypedBuiltinFunction(string name)
        {
            Name = name;
        }

        public abstract object? Call(Executor executor, List<object?> arguments);

        /// <summary>
        /// Validates and converts arguments to expected types
        /// </summary>
        protected object?[] ValidateAndConvertArguments(List<object?> arguments)
        {
            if (arguments.Count != ParameterTypes.Length)
            {
                throw new RuntimeException(
                    $"{Name}() expects {ParameterTypes.Length} argument(s), but got {arguments.Count}");
            }

            var converted = new object?[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
            {
                converted[i] = ConvertArgument(arguments[i], ParameterTypes[i], i);
            }
            return converted;
        }

        private object? ConvertArgument(object? value, Type targetType, int argIndex)
        {
            if (value == null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                {
                    throw new RuntimeException(
                        $"{Name}() argument {argIndex + 1} cannot be null (expected {targetType.Name})");
                }
                return null;
            }

            if (targetType.IsAssignableFrom(value.GetType()))
                return value;

            // Try built-in conversions
            try
            {
                if (targetType == typeof(int))
                    return Convert.ToInt32(value);
                if (targetType == typeof(string))
                    return value.ToString();
                if (targetType == typeof(bool))
                    return Convert.ToBoolean(value);
                if (targetType == typeof(double))
                    return Convert.ToDouble(value);

                return Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                throw new RuntimeException(
                    $"{Name}() argument {argIndex + 1}: cannot convert {value.GetType().Name} to {targetType.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Optimized wait function with compile-time type checking
    /// </summary>
    public class WaitFunction : TypedBuiltinFunction
    {
        public override Type[] ParameterTypes => new[] { typeof(int) };
        public override Type ReturnType => typeof(void);

        public WaitFunction() : base("wait") { }

        public override object? Call(Executor executor, List<object?> arguments)
        {
            var args = ValidateAndConvertArguments(arguments);
            var milliseconds = (int)args[0]!;

            if (milliseconds < 0)
            {
                throw new RuntimeException("wait() milliseconds must be non-negative");
            }

            Thread.Sleep(milliseconds);
            return null;
        }
    }

    /// <summary>
    /// Optimized read_file function with proper error context
    /// </summary>
    public class ReadFileFunction : TypedBuiltinFunction
    {
        public override Type[] ParameterTypes => new[] { typeof(string) };
        public override Type ReturnType => typeof(string);

        public ReadFileFunction() : base("read_file") { }

        public override object? Call(Executor executor, List<object?> arguments)
        {
            var args = ValidateAndConvertArguments(arguments);
            var filePath = (string)args[0]!;

            try
            {
                if (!File.Exists(filePath))
                {
                    var context = executor.CreateErrorContext();
                    throw context.CreateRuntimeException($"File not found: {filePath}");
                }

                return File.ReadAllText(filePath);
            }
            catch (IOException ex)
            {
                var context = executor.CreateErrorContext();
                throw context.CreateRuntimeException($"Error reading file '{filePath}': {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Optimized instantiate function with type caching
    /// </summary>
    public class InstantiateFunction : TypedBuiltinFunction
    {
        private static readonly ConcurrentDictionary<string, Type?> _typeCache = new();
        private static readonly ConcurrentDictionary<(Type, int), ConstructorInfo?> _constructorCache = new();

        public override Type[] ParameterTypes => new[] { typeof(string), typeof(object[]) };
        public override Type ReturnType => typeof(object);

        public InstantiateFunction() : base("instantiate") { }

        public override object? Call(Executor executor, List<object?> arguments)
        {
            if (arguments.Count < 1 || arguments.Count > 2)
            {
                throw new RuntimeException("instantiate() expects 1-2 arguments (type name, optional constructor args)");
            }

            var typeName = arguments[0]?.ToString();
            if (string.IsNullOrEmpty(typeName))
            {
                throw new RuntimeException("instantiate() type name cannot be null or empty");
            }

            var constructorArgs = arguments.Count > 1 ? 
                (arguments[1] as object[] ?? new[] { arguments[1] }) : 
                Array.Empty<object>();

            try
            {
                var type = GetTypeFromCache(typeName);
                if (type == null)
                {
                    throw new RuntimeException($"Type '{typeName}' not found");
                }

                var constructor = GetConstructorFromCache(type, constructorArgs.Length);
                if (constructor == null)
                {
                    throw new RuntimeException($"No constructor found for type '{typeName}' with {constructorArgs.Length} parameters");
                }

                // Convert arguments to match constructor parameter types
                var paramTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();
                var convertedArgs = new object[constructorArgs.Length];
                
                for (int i = 0; i < constructorArgs.Length; i++)
                {
                    convertedArgs[i] = ConvertArgument(constructorArgs[i], paramTypes[i], i);
                }

                return constructor.Invoke(convertedArgs);
            }
            catch (Exception ex) when (!(ex is RuntimeException))
            {
                var context = executor.CreateErrorContext();
                throw context.CreateRuntimeException($"Failed to instantiate '{typeName}': {ex.Message}", ex);
            }
        }

        private Type? GetTypeFromCache(string typeName)
        {
            return _typeCache.GetOrAdd(typeName, name =>
            {
                // Try direct type lookup first
                var type = Type.GetType(name);
                if (type != null) return type;

                // Try current assembly
                type = Assembly.GetExecutingAssembly().GetType(name);
                if (type != null) return type;

                // Try all loaded assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        type = assembly.GetType(name);
                        if (type != null) return type;
                    }
                    catch
                    {
                        // Ignore assembly loading errors
                    }
                }

                // Try loading assembly based on namespace
                try
                {
                    var namespacePart = name.Substring(0, name.LastIndexOf('.'));
                    var assembly = Assembly.LoadFrom($"{namespacePart}.dll");
                    type = assembly.GetType(name);
                    if (type != null) return type;
                }
                catch
                {
                    // Ignore assembly loading errors
                }

                return null; // Type not found
            });
        }

        private ConstructorInfo? GetConstructorFromCache(Type type, int parameterCount)
        {
            return _constructorCache.GetOrAdd((type, parameterCount), key =>
            {
                var (t, count) = key;
                return t.GetConstructors()
                    .FirstOrDefault(c => c.GetParameters().Length == count);
            });
        }

        private object? ConvertArgument(object? value, Type targetType, int argIndex)
        {
            if (value == null) return null;
            if (targetType.IsAssignableFrom(value.GetType())) return value;

            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch (Exception)
            {
                throw new RuntimeException(
                    $"instantiate() constructor argument {argIndex + 1}: cannot convert {value.GetType().Name} to {targetType.Name}");
            }
        }
    }

    /// <summary>
    /// Optimized built-in function registry with pre-compiled dispatch
    /// </summary>
    public static class OptimizedBuiltinRegistry
    {
        private static readonly Dictionary<string, TypedBuiltinFunction> _functions = new()
        {
            ["wait"] = new WaitFunction(),
            ["read_file"] = new ReadFileFunction(),
            ["instantiate"] = new InstantiateFunction(),
        };

        /// <summary>
        /// Registers all optimized built-in functions with the executor
        /// </summary>
        public static void RegisterOptimizedFunctions(Executor executor)
        {
            foreach (var (name, function) in _functions)
            {
                executor.RegisterBuiltinFunction(name, function);
            }
        }

        /// <summary>
        /// Gets a built-in function by name for direct dispatch
        /// </summary>
        public static TypedBuiltinFunction? GetFunction(string name)
        {
            _functions.TryGetValue(name, out var function);
            return function;
        }

        /// <summary>
        /// Gets all registered function names
        /// </summary>
        public static IEnumerable<string> GetFunctionNames() => _functions.Keys;
    }
}