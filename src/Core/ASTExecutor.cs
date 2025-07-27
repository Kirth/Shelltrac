using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection; // just for BindingFlags
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shelltrac
{
    // Caching infrastructure for @cache attribute
    public class CacheEntry
    {
        public object? Value { get; }
        public DateTime ExpiryTime { get; }
        public bool IsExpired => DateTime.UtcNow > ExpiryTime;

        public CacheEntry(object? value, long ttlMilliseconds)
        {
            Value = value;
            ExpiryTime = DateTime.UtcNow.AddMilliseconds(ttlMilliseconds);
        }
    }

    public static class FunctionCache
    {
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

        public static bool TryGet(string key, out object? value)
        {
            value = null;
            if (_cache.TryGetValue(key, out var entry))
            {
                if (!entry.IsExpired)
                {
                    value = entry.Value;
                    return true;
                }
                else
                {
                    // Remove expired entry
                    _cache.TryRemove(key, out _);
                }
            }
            return false;
        }

        public static void Set(string key, object? value, long ttlMilliseconds)
        {
            _cache[key] = new CacheEntry(value, ttlMilliseconds);
        }

        public static void Clear()
        {
            _cache.Clear();
        }

        // Generate cache key from function name and arguments
        public static string GenerateKey(string functionName, List<object?> arguments)
        {
            var keyBuilder = new StringBuilder();
            keyBuilder.Append(functionName);
            keyBuilder.Append('(');
            for (int i = 0; i < arguments.Count; i++)
            {
                if (i > 0) keyBuilder.Append(',');
                keyBuilder.Append(arguments[i]?.ToString() ?? "null");
            }
            keyBuilder.Append(')');
            return keyBuilder.ToString();
        }
    }

    public class ExecutionScope
    {
        // Variable storage - now immutable
        public ImmutableDictionary<string, object?> Variables { get; }

        // Execution tracking
        public SourceLocation Location { get; set; }

        public ExecutionScope(
            SourceLocation location,
            ImmutableDictionary<string, object?>? parentVariables = null
        )
        {
            Location = location;

            // Inherit variables from parent scope or start with empty
            Variables = parentVariables ?? ImmutableDictionary<string, object?>.Empty;
        }

        // Helper method to create a new scope with updated variables
        public ExecutionScope WithVariable(string name, object? value)
        {
            return new ExecutionScope(Location, Variables.SetItem(name, value));
        }

        // Helper method to create a new scope with multiple variables
        public ExecutionScope WithVariables(IEnumerable<KeyValuePair<string, object?>> variables)
        {
            var newVariables = Variables;
            foreach (var kvp in variables)
            {
                newVariables = newVariables.SetItem(kvp.Key, kvp.Value);
            }
            return new ExecutionScope(Location, newVariables);
        }
    }

    public class ExecutionContext
    {
        private readonly List<ExecutionScope> _scopeStack = new List<ExecutionScope>();
        private readonly string _scriptName;
        private readonly string _sourceCode;

        // Direct access to global scope for built-in functions
        public ExecutionScope GlobalScope => _scopeStack[0];

        // Current scope always refers to the topmost scope on the stack
        public ExecutionScope CurrentScope => _scopeStack[_scopeStack.Count - 1];

        // Current source location for error reporting
        public SourceLocation CurrentLocation => CurrentScope.Location;

        public ExecutionContext(string scriptName, string sourceCode)
        {
            _scriptName = scriptName;
            _sourceCode = sourceCode;

            // Initialize with a global scope
            _scopeStack.Add(
                new ExecutionScope(new SourceLocation(1, 1, "Script start", scriptName))
            );
        }

        // Push a new scope with the current location
        public void PushScope(Stmt? statement = null, bool inheritVariables = false)
        {
            SourceLocation location;

            if (statement != null)
                location = new SourceLocation(statement, _scriptName);
            else
                location = new SourceLocation(
                    CurrentLocation.Line,
                    CurrentLocation.Column,
                    "Anonymous scope",
                    _scriptName
                );

            var parentVars = inheritVariables ? CurrentScope.Variables : ImmutableDictionary<string, object?>.Empty;
            _scopeStack.Add(new ExecutionScope(location, parentVars));
        }

        // Push location tracking without creating a new variable scope
        public void PushLocation(Stmt stmt)
        {
            CurrentScope.Location = new SourceLocation(
                stmt.Line,
                stmt.Column,
                $"{stmt.GetType().Name}",
                _scriptName
            );
        }

        public void PushLocation(Expr expr)
        {
            CurrentScope.Location = new SourceLocation(
                expr.Line,
                expr.Column,
                $"{expr.GetType().Name}",
                _scriptName
            );
        }

        // Pop the topmost scope
        public void PopScope()
        {
            if (_scopeStack.Count > 1)
            {
                _scopeStack.RemoveAt(_scopeStack.Count - 1);
            }
        }

        // Restore the previous location
        public void PopLocation()
        {
            // In our implementation, location is stored in the scope,
            // so this is a no-op unless we maintain a location stack
        }

        // Look up a variable by traversing the scope stack from top to bottom
        public object? LookupVariable(string name)
        {
            for (int i = _scopeStack.Count - 1; i >= 0; i--)
            {
                if (_scopeStack[i].Variables.TryGetValue(name, out var value))
                {
                    return value;
                }
            }
            return null;
        }

        // Get all variables (for parallel task isolation)
        public ImmutableDictionary<string, object?> GetAllVariables()
        {
            var result = ImmutableDictionary<string, object?>.Empty;

            // Start with globals and override with more local scopes
            for (int i = 0; i < _scopeStack.Count; i++)
            {
                foreach (var kvp in _scopeStack[i].Variables)
                {
                    result = result.SetItem(kvp.Key, kvp.Value);
                }
            }

            return result;
        }

        // Assign to an existing variable in any scope
        public bool AssignVariable(string name, object? value)
        {
            for (int i = _scopeStack.Count - 1; i >= 0; i--)
            {
                if (_scopeStack[i].Variables.ContainsKey(name))
                {
                    // Create new scope with updated variable
                    var oldScope = _scopeStack[i];
                    var newScope = new ExecutionScope(oldScope.Location, oldScope.Variables.SetItem(name, value));
                    _scopeStack[i] = newScope;
                    return true;
                }
            }
            return false;
        }

        // Add a variable to the current scope
        public void SetCurrentScopeVariable(string name, object? value)
        {
            var currentScope = CurrentScope;
            var newScope = new ExecutionScope(currentScope.Location, currentScope.Variables.SetItem(name, value));
            _scopeStack[_scopeStack.Count - 1] = newScope;
        }

        // Reset scopes for parallel execution with a new base environment
        public void ResetScopes(ImmutableDictionary<string, object?> baseEnvironment)
        {
            _scopeStack.Clear();
            _scopeStack.Add(new ExecutionScope(
                new SourceLocation(1, 1, "Parallel execution start", _scriptName),
                baseEnvironment
            ));
        }

        // Replace the current scope with a new one containing additional variables
        public void UpdateCurrentScopeWithVariables(IEnumerable<KeyValuePair<string, object?>> variables)
        {
            var currentScope = CurrentScope;
            var newVariables = currentScope.Variables;
            foreach (var kvp in variables)
            {
                newVariables = newVariables.SetItem(kvp.Key, kvp.Value);
            }
            var newScope = new ExecutionScope(currentScope.Location, newVariables);
            _scopeStack[_scopeStack.Count - 1] = newScope;
        }

        // Extract source code fragment for error context
        public string GetContextFragment(int line, int contextLines = 1)
        {
            if (string.IsNullOrEmpty(_sourceCode) || line <= 0)
                return string.Empty;

            string[] lines = _sourceCode.Split('\n');
            if (line > lines.Length)
                return string.Empty;

            int startLine = Math.Max(1, line - contextLines);
            int endLine = Math.Min(lines.Length, line + contextLines);

            var result = new StringBuilder();

            // Add a header showing file location
            result.AppendLine($"In {_scriptName}:");

            // Show context lines with line numbers
            for (int i = startLine - 1; i < endLine; i++)
            {
                // Add an arrow indicator for the error line
                string linePrefix = (i == line - 1) ? "→ " : "  ";

                // Add line number and code
                result.AppendLine($"{linePrefix}{i + 1}: {lines[i]}");

                // Add error position indicator with caret
                if (i == line - 1)
                {
                    int column = Math.Max(1, Math.Min(lines[i].Length + 1, CurrentLocation.Column));
                    // The +2 compensates for the line number display
                    result.AppendLine($"    {new string(' ', column)}^");
                }
            }

            return result.ToString();
        }
    }

    public class Executor : IStmtVisitor, IExprVisitor<object?>
    {
        private readonly Dictionary<string, List<EventHandler>> _eventHandlers = new();
        private readonly ExecutionContext _context;

        public ExecutionContext Context => _context; // public accessor.. don't wanna redo all the _context shit
        private readonly string _scriptPath;
        private readonly string _sourceCode;

        /// <summary>
        /// Creates a new Shelltrac script executor
        /// </summary>
        /// <param name="scriptPath">Path of the script being executed</param>
        /// <param name="sourceCode">Source code of the script</param>
        public Executor(string scriptPath = "script", string sourceCode = "")
        {
            _scriptPath = scriptPath;
            _sourceCode = sourceCode;
            _context = new ExecutionContext(scriptPath, sourceCode);

            OptimizedBuiltinRegistry.RegisterOptimizedFunctions(this);
        }

        public void RegisterBuiltinFunction(string name, Callable function)
        {
            _context.SetCurrentScopeVariable(name, function);
        }

        public void PushEnvironment(Dictionary<string, object?> env)
        {
            _context.PushScope();
            _context.UpdateCurrentScopeWithVariables(env);
        }

        public void PopEnvironment()
        {
            _context.PopScope();
        }

        /// <summary>
        /// Executes the entire program
        /// </summary>
        /// <param name="program">The parsed program AST</param>
        public void Execute(ProgramNode program)
        {
#if PERF_TRACKING
            var executionSw = System.Diagnostics.Stopwatch.StartNew();
            var stmtExecutionTimes = new List<(string, long)>();
            var taskCount = 0;
            var functionDefCount = 0;
            var eventDefCount = 0;
            var regularStmtCount = 0;
#endif

            // Lists for once-tasks and fire-and-forget every-tasks
            var onceTasks = new List<Task>();

            // Process statements in order
            foreach (var stmt in program.Statements)
            {
#if PERF_TRACKING
                var stmtSw = System.Diagnostics.Stopwatch.StartNew();
#endif
                try
                {
                    if (stmt is TaskStmt task)
                    {
#if PERF_TRACKING
                        taskCount++;
#endif
                        if (task.IsOnce)
                        {
                            // Run once tasks in parallel
                            onceTasks.Add(Task.Run(() => ExecuteBlock(task.Body)));
                        }
                        else
                        {
                            // For every tasks, schedule an endless loop with a delay
                            Task.Run(async () =>
                            {
                                while (true)
                                {
                                    ExecuteBlock(task.Body);
                                    await Task.Delay(task.Frequency);
                                }
                            });
                        }
                    }
                    else if (stmt is EventStmt ev)
                    {
#if PERF_TRACKING
                        eventDefCount++;
#endif
                        if (!_eventHandlers.ContainsKey(ev.EventName))
                            _eventHandlers[ev.EventName] = new List<EventHandler>();

                        _eventHandlers[ev.EventName].Add(new EventHandler(ev.Parameters, ev.Body));
                    }
                    else if (stmt is FunctionStmt)
                    {
#if PERF_TRACKING
                        functionDefCount++;
#endif
                        ExecuteStmt(stmt);
                    }
                    else
                    {
#if PERF_TRACKING
                        regularStmtCount++;
#endif
                        // For all other statements, execute them immediately
                        ExecuteStmt(stmt);
                    }
                }
                catch (ShelltracException ex)
                {
                    Console.Error.WriteLine($"[ERROR] {ex}");
                    // Continue execution despite errors in top-level statements
                }
#if PERF_TRACKING
                finally
                {
                    stmtSw.Stop();
                    stmtExecutionTimes.Add((stmt.GetType().Name, stmtSw.ElapsedMilliseconds));
                }
#endif
            }

            // Wait for all once-tasks to complete
            try
            {
                Task.WaitAll(onceTasks.ToArray());
            }
            catch (AggregateException ae)
            {
                // Report all task exceptions
                foreach (var ex in ae.InnerExceptions)
                {
                    if (ex is ShelltracException sEx)
                    {
                        Console.Error.WriteLine($"[TASK ERROR] {sEx}");
                    }
                    else
                    {
                        Console.Error.WriteLine($"[TASK ERROR] {ex.Message}");
                    }
                }
            }

#if PERF_TRACKING
            executionSw.Stop();
            Console.WriteLine($"[PERF] Execution breakdown - Total: {executionSw.ElapsedMilliseconds}ms");
            Console.WriteLine($"[PERF]   Tasks: {taskCount}, Functions: {functionDefCount}, Events: {eventDefCount}, Regular: {regularStmtCount}");
            
            var slowStatements = stmtExecutionTimes.Where(x => x.Item2 > 1).OrderByDescending(x => x.Item2).Take(5);
            foreach (var (stmtType, time) in slowStatements)
            {
                Console.WriteLine($"[PERF]   Slow stmt: {stmtType} took {time}ms");
            }
#endif
        }

        /// <summary>
        /// Execute a single statement
        /// </summary>
        public void ExecuteStmt(Stmt stmt)
        {
            try
            {
                // Track statement location for error reporting
                _context.PushLocation(stmt);

                // Use visitor pattern to dispatch statement execution
                stmt.Accept(this);
            }
            catch (ShelltracException)
            {
                // Let structured exceptions propagate with their context intact
                throw;
            }
            catch (ReturnException)
            {
                // Let control flow exceptions propagate
                throw;
            }
            catch (YieldException)
            {
                // Let control flow exceptions propagate
                throw;
            }
            catch (Exception ex)
            {
                // Convert generic exceptions to our structured format
                var location = _context.CurrentLocation;
                string context = _context.GetContextFragment(location.Line);

                throw new RuntimeException(
                    $"Error executing {stmt.GetType().Name}: {ex.Message}",
                    location.Line,
                    location.Column,
                    context,
                    ex
                );
            }
            finally
            {
                _context.PopLocation();
            }
        }

        /// <summary>
        /// Execute a block of statements with their own scope
        /// </summary>
        public void ExecuteBlock(List<Stmt> block)
        {
            // Create a new scope for this block
            _context.PushScope();

            try
            {
                foreach (var stmt in block)
                {
                    ExecuteStmt(stmt);
                }
            }
            finally
            {
                // Always clean up the scope
                _context.PopScope();
            }
        }

        #region Statement Handlers

        private void HandleTrigger(TriggerStmt trigger)
        {
            if (_eventHandlers.TryGetValue(trigger.EventName, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    if (handler.Parameters.Count != trigger.Arguments.Count)
                        throw new RuntimeException(
                            $"Event {trigger.EventName} expects {handler.Parameters.Count} arguments, but got {trigger.Arguments.Count}",
                            trigger.Line,
                            trigger.Column
                        );

                    // Evaluate each argument
                    List<object?> evaluatedArgs = new List<object?>();
                    foreach (var argExpr in trigger.Arguments)
                        evaluatedArgs.Add(Eval(argExpr));

                    // Create a new scope for the event handler with parameter bindings
                    _context.PushScope();

                    try
                    {
                        // Bind parameters to values
                        for (int i = 0; i < handler.Parameters.Count; i++)
                        {
                            _context.SetCurrentScopeVariable(handler.Parameters[i], evaluatedArgs[i]);
                        }

                        // Execute the handler body
                        ExecuteBlock(handler.Body);
                    }
                    finally
                    {
                        _context.PopScope();
                    }
                }
            }
            else
            {
                Console.WriteLine($"[Warning] No handlers for event '{trigger.EventName}'");
            }
        }

        private void HandleInvocation(InvocationStmt inv)
        {
#if PERF_TRACKING
            var invSw = System.Diagnostics.Stopwatch.StartNew();
            var evalSw = System.Diagnostics.Stopwatch.StartNew();
#endif
            
            // Evaluate the argument expression
            object val = Eval(inv.Argument)!;
#if PERF_TRACKING
            evalSw.Stop();
            
            var execSw = System.Diagnostics.Stopwatch.StartNew();
#endif
            
            switch (inv.CommandKeyword)
            {
                case "log":
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {Helper.ToPrettyString(val)}");
                    break;

                case "echo":
                    Console.WriteLine(Helper.ToPrettyString(val));
                    break;

                case "ssh":
                    if (inv.Argument is SshExpr sshExpr)
                    {
                        // Execute SSH with error handling
                        ExecuteSshCommand(sshExpr);
                    }
                    else
                    {
                        throw new RuntimeException(
                            "SSH invocation requires host and command",
                            inv.Line,
                            inv.Column
                        );
                    }
                    break;

                case "sh":
                    string command = val?.ToString() ?? "";
                    ExecuteShellCommand(command, null, inv.Line, inv.Column);
                    break;
            }
#if PERF_TRACKING
            execSw.Stop();
            invSw.Stop();
            
            if (invSw.ElapsedMilliseconds > 10)
            {
                Console.WriteLine($"[PERF] Slow invocation '{inv.CommandKeyword}': total={invSw.ElapsedMilliseconds}ms (eval={evalSw.ElapsedMilliseconds}ms, exec={execSw.ElapsedMilliseconds}ms)");
            }
#endif
        }

        private void HandleIndexAssign(IndexAssignStmt stmt)
        {
            object target = Eval(stmt.Target)!;
            object index = Eval(stmt.Index)!;
            object value = Eval(stmt.ValueExpr)!;

            // Support dictionary assignment
            if (target is IDictionary<string, object> dict)
            {
                string key =
                    index?.ToString()
                    ?? throw new RuntimeException(
                        "Dictionary key cannot be null",
                        stmt.Line,
                        stmt.Column
                    );
                dict[key] = value;
            }
            // Support list assignment
            else if (target is IList<object> list)
            {
                int i = ConvertToInt(index);
                if (i < 0 || i >= list.Count)
                    throw new RuntimeException(
                        $"Index {i} is out of range for list of length {list.Count}",
                        stmt.Line,
                        stmt.Column
                    );
                list[i] = value;
            }
            else
            {
                throw new RuntimeException(
                    $"Type {target?.GetType().Name ?? "null"} does not support index assignment",
                    stmt.Line,
                    stmt.Column
                );
            }
        }

        private void HandleDestructuringAssign(DestructuringAssignStmt stmt)
        {
            object? value = Eval(stmt.ValueExpr);
            List<object?> values;

            // Convert the value to a list of values
            if (value is List<object?> list)
            {
                values = list;
            }
            else if (value is IEnumerable<object> enumerable && !(value is string))
            {
                values = enumerable.Cast<object?>().ToList();
            }
            else
            {
                // Single value - wrap in a list
                values = new List<object?> { value };
            }

            // Assign values to variables, skipping wildcards
            int count = Math.Min(stmt.VarNames.Count, values.Count);

            Debug.Assert(count > 2, "count for DestructuringAssignStmt is smaller than 2");

            for (int i = 0; i < count; i++)
            {
                string varName = stmt.VarNames[i];
                if (varName != "_") // Skip _ wildcards
                {
                    _context.SetCurrentScopeVariable(varName, values[i]);
                }
            }
        }

        private void HandleIf(IfStmt ifs)
        {
            object condVal = Eval(ifs.Condition)!;
            bool condition = ConvertToBool(condVal)!;

            if (condition)
            {
                ExecuteBlock(ifs.ThenBlock);
            }
            else if (ifs.ElseBlock != null)
            {
                ExecuteBlock(ifs.ElseBlock);
            }
        }

        private void HandleFor(ForStmt fs)
        {
            object iterable = Eval(fs.Iterable)!;

            if (!(iterable is IEnumerable enumerable))
            {
                throw new RuntimeException(
                    $"For loop requires an enumerable value, got {iterable?.GetType().Name ?? "null"}",
                    fs.Line,
                    fs.Column
                );
            }

            foreach (object item in enumerable)
            {
                // Create a new scope for this iteration
                _context.PushScope();

                try
                {
                    // Set the loop variable
                    _context.SetCurrentScopeVariable(fs.IteratorVar, item);

                    // Execute the body
                    ExecuteBlock(fs.Body);
                }
                catch (Exception ex) when (!(ex is ReturnException || ex is YieldException))
                {
                    // Log errors but continue loop unless it's a control flow exception
                    Console.Error.WriteLine($"[FOR LOOP ERROR] {ex.Message}");
                }
                finally
                {
                    // Always clean up the scope
                    _context.PopScope();
                }
            }
        }

        private void HandleWhile(WhileStmt ws)
        {
            const int MAX_ITERATIONS = 100000; // Prevent infinite loops
            int iterations = 0;

            while (true)
            {
                iterations++;
                if (iterations > MAX_ITERATIONS)
                {
                    throw new RuntimeException(
                        $"While loop exceeded maximum iterations ({MAX_ITERATIONS}). Possible infinite loop.",
                        ws.Line,
                        ws.Column
                    );
                }

                // Evaluate the condition
                object? conditionResult = Eval(ws.Condition);
                bool isTruthy = ConvertToBool(conditionResult!);

                if (!isTruthy)
                {
                    break; // Exit the loop
                }

                // Create a new scope for this iteration
                _context.PushScope();

                try
                {
                    // Execute the body
                    ExecuteBlock(ws.Body);
                }
                catch (Exception ex) when (!(ex is ReturnException || ex is YieldException))
                {
                    // Rethrow errors to stop loop execution
                    throw;
                }
                finally
                {
                    // Always clean up the scope
                    _context.PopScope();
                }
            }
        }

        #endregion

        #region Statement Visitor Methods

        public void Visit(ExpressionStmt stmt)
        {
            Eval(stmt.Expression);
        }

        public void Visit(TaskStmt stmt)
        {
            // No-op here, handled in Execute method
        }

        public void Visit(EventStmt stmt)
        {
            // No-op here, handled in Execute method
        }

        public void Visit(FunctionStmt stmt)
        {
            // Store function in the current scope
            _context.SetCurrentScopeVariable(stmt.Name, new Function(
                stmt,
                _context.GetAllVariables()
            ));
        }

        public void Visit(ReturnStmt stmt)
        {
            List<object?> returnValues = new List<object?>();
            foreach (var valueExpr in stmt.Values)
            {
                returnValues.Add(Eval(valueExpr));
            }
            throw new ReturnException(returnValues);
        }

        public void Visit(TriggerStmt stmt)
        {
            HandleTrigger(stmt);
        }

        public void Visit(DestructuringAssignStmt stmt)
        {
            HandleDestructuringAssign(stmt);
        }

        public void Visit(InvocationStmt stmt)
        {
            HandleInvocation(stmt);
        }

        public void Visit(VarDeclStmt stmt)
        {
            // Always put in current scope
            object val = Eval(stmt.Initializer)!;
            _context.SetCurrentScopeVariable(stmt.VarName, val);
        }

        public void Visit(AssignStmt stmt)
        {
            object rhsVal = Eval(stmt.ValueExpr)!;
            if (!_context.AssignVariable(stmt.VarName, rhsVal))
            {
                var context = CreateErrorContext(stmt.Line, stmt.Column);
                throw context.CreateRuntimeException($"Variable '{stmt.VarName}' not found.");
            }
        }

        public void Visit(IndexAssignStmt stmt)
        {
            HandleIndexAssign(stmt);
        }

        public void Visit(IfStmt stmt)
        {
            HandleIf(stmt);
        }

        public void Visit(ForStmt stmt)
        {
            HandleFor(stmt);
        }

        public void Visit(WhileStmt stmt)
        {
            HandleWhile(stmt);
        }

        public void Visit(LoopYieldStmt stmt)
        {
            object? yieldValue = stmt.Value != null ? Eval(stmt.Value) : null;
            throw new YieldException(
                yieldValue,
                stmt.IsEmit,
                stmt.IsGlobalCancel,
                stmt.IsOverride
            );
        }

        #endregion

        #region Expression Evaluation

        /// <summary>
        /// Evaluates an expression to its value
        /// </summary>
        public object? Eval(Expr expr)
        {
            try
            {
                // Track expression location for error reporting
                _context.PushLocation(expr);

                // Use visitor pattern to dispatch expression evaluation
                return expr.Accept(this);
            }
            catch (ShelltracException)
            {
                // Let structured exceptions propagate
                throw;
            }
            catch (Exception ex)
            {
                // Convert generic exceptions to our structured format
                var location = _context.CurrentLocation;
                string context = _context.GetContextFragment(location.Line);

                throw new RuntimeException(
                    $"Error evaluating {expr.GetType().Name}: {ex.Message}",
                    location.Line,
                    location.Column,
                    context,
                    ex
                );
            }
            finally
            {
                _context.PopLocation();
            }
        }

        private object EvalParallelFor(ParallelForExpr pf)
        {
            var iterable = Eval(pf.Iterable);
            if (!(iterable is IEnumerable en))
                throw new RuntimeException(
                    "Parallel for requires an enumerable",
                    pf.Line,
                    pf.Column
                );

            var items = new List<object>();
            foreach (var item in en)
                items.Add(item);

            var cts = new CancellationTokenSource();
            var results = new ConcurrentBag<object?>();
            var errors = new ConcurrentBag<Exception>();
            var firstCancelResult = null as object;
            var hasOverride = false;

            var tasks = items
                .Select(item =>
                    Task.Run(
                        () =>
                        {
                            // Use shared executor with isolated immutable state
                            var capturedEnvironment = _context.GetAllVariables();
                            var environmentWithLoopVar = capturedEnvironment.SetItem(pf.IteratorVar, item);

                            // Create a temporary isolated executor for this task
                            var isolatedExecutor = new Executor(_scriptPath, _sourceCode);
                            isolatedExecutor._context.ResetScopes(environmentWithLoopVar);

                            try
                            {
                                // Execute each statement, watching for yield/return
                                foreach (var stmt in pf.Body)
                                {
                                    if (cts.Token.IsCancellationRequested)
                                        break;

                                    try
                                    {
                                        isolatedExecutor.ExecuteStmt(stmt);
                                    }
                                    catch (YieldException ye)
                                    {
                                        if (ye.IsEmit)
                                        {
                                            // Add to results and continue
                                            results.Add(ye.Value);
                                        }
                                        else
                                        {
                                            // Add to results and terminate this iteration
                                            results.Add(ye.Value);

                                            if (ye.IsGlobalCancel)
                                            {
                                                lock (cts)
                                                {
                                                    if (!cts.IsCancellationRequested)
                                                    {
                                                        firstCancelResult = ye.Value;
                                                        hasOverride = ye.IsOverride;
                                                        cts.Cancel();
                                                    }
                                                }
                                            }

                                            // Break out of the statement loop
                                            break;
                                        }
                                    }
                                    catch (ReturnException re)
                                    {
                                        // Add the return value and exit
                                        results.Add(re.Values[0]);
                                        break;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // Capture any other exception
                                errors.Add(ex);
                            }
                        },
                        cts.Token
                    )
                )
                .ToArray();

            try
            {
                Task.WaitAll(tasks);
            }
            catch (AggregateException ae)
            {
                // Collect task errors
                foreach (var ex in ae.InnerExceptions)
                {
                    errors.Add(ex);
                }
            }

            // Report any errors
            if (errors.Count > 0)
            {
                var location = _context.CurrentLocation;

                if (errors.Count == 1)
                {
                    var error = errors.First();
                    if (error is ShelltracException sEx)
                        throw sEx;

                    throw new RuntimeException(
                        $"Error in parallel task: {error.Message}",
                        location.Line,
                        location.Column,
                        "",
                        error
                    );
                }
                else
                {
                    // Aggregate multiple errors
                    StringBuilder errorMsg = new StringBuilder(
                        $"{errors.Count} errors in parallel execution:"
                    );
                    foreach (var error in errors.Take(3))
                    {
                        errorMsg.AppendLine();
                        errorMsg.Append("- " + error.Message);
                    }

                    if (errors.Count > 3)
                        errorMsg.AppendLine($"\n- ... and {errors.Count - 3} more errors");

                    throw new RuntimeException(errorMsg.ToString(), location.Line, location.Column);
                }
            }

            return hasOverride ? firstCancelResult! : results.ToList();
        }

        private object? EvaluateBlockExpression(List<Stmt> block)
        {
            _context.PushScope();
            object? lastValue = null;

            try
            {
                foreach (var stmt in block)
                {
                    if (stmt is ExpressionStmt exprStmt)
                        lastValue = Eval(exprStmt.Expression);
                    else
                        ExecuteStmt(stmt);
                }
                return lastValue;
            }
            catch (ReturnException re)
            {
                return re.Values[0];
            }
            finally
            {
                _context.PopScope();
            }
        }

        private object? BindMember(object parent, string memberName)
        {
            if (parent == null)
                throw new RuntimeException(
                    $"Cannot access member '{memberName}' of null",
                    _context.CurrentLocation.Line,
                    _context.CurrentLocation.Column
                );

            // Fast path optimizations for common types to avoid reflection overhead
            if (parent is TimeLiteralExpr timeLiteral)
            {
                return memberName.ToLowerInvariant() switch
                {
                    "totalmilliseconds" => timeLiteral.TotalMilliseconds,
                    "value" => timeLiteral.Value,
                    "unit" => timeLiteral.Unit,
                    _ => throw new RuntimeException(
                        $"Member '{memberName}' not found on TimeLiteralExpr",
                        _context.CurrentLocation.Line,
                        _context.CurrentLocation.Column
                    )
                };
            }

            var type = parent.GetType();
            var key = (type, memberName);

            // Get or compile member access
            var compiledAccess = _memberCache.GetOrAdd(key, k => CompileMemberAccess(k.Item1, k.Item2));

            switch (compiledAccess.Type)
            {
                case MemberType.Method:
                    return new ReflectionCallable(parent, compiledAccess.Methods!);

                case MemberType.ExtensionMethod:
                    return new ExtensionMethodCallable(parent, compiledAccess.ExtensionMethods!);

                case MemberType.Property:
                    if (compiledAccess.CompiledProperty != null)
                    {
                        // Use compiled property accessor for faster access
                        return compiledAccess.CompiledProperty(parent);
                    }
                    else
                    {
                        // Fallback to reflection if compilation failed
                        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                        return property?.GetValue(parent);
                    }

                case MemberType.Field:
                    if (compiledAccess.CompiledProperty != null) // Using CompiledProperty for field access too
                    {
                        // Use compiled field accessor for faster access
                        return compiledAccess.CompiledProperty(parent);
                    }
                    else
                    {
                        // Fallback to reflection if compilation failed
                        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                        return field?.GetValue(parent);
                    }

                case MemberType.None:
                default:
                    // Try UFCS resolution before giving up
                    var ufcsCallable = TryResolveUFCS(parent, memberName);
                    if (ufcsCallable != null)
                    {
                        return ufcsCallable;
                    }
                    
                    throw new RuntimeException(
                        $"Member '{memberName}' not found on object of type {type.Name}",
                        _context.CurrentLocation.Line,
                        _context.CurrentLocation.Column
                    );
            }
        }

        private Callable? TryResolveUFCS(object target, string memberName)
        {
            // Look for a function with the given name in the current context
            try
            {
                var function = _context.LookupVariable(memberName);
                if (function is Callable callable)
                {
                    // Check if this function can accept the target as its first parameter
                    // For now, we'll assume it can and let the actual call handle type checking
                    return new UFCSCallable(target, callable);
                }
            }
            catch (RuntimeException)
            {
                // Function not found in scope, continue with normal error handling
            }
            
            return null;
        }

        private object EvalBinary(BinaryExpr bin)
        {
            object leftVal = Eval(bin.Left)!;
            object rightVal = Eval(bin.Right)!;

            switch (bin.Op)
            {
                case "<":
                    return CompareLess(leftVal, rightVal);
                case "<=":
                    return CompareLessEqual(leftVal, rightVal);
                case ">":
                    return CompareGreater(leftVal, rightVal);
                case ">=":
                    return CompareGreaterEqual(leftVal, rightVal);
                case "==":
                    return CompareEqual(leftVal, rightVal);
                case "!=":
                    return CompareNotEqual(leftVal, rightVal);
                case "+":
                    return AddValues(leftVal, rightVal);
                case "*":
                    return MultiplyValues(leftVal, rightVal, bin.Line, bin.Column);
                case "/":
                    return DivideValues(leftVal, rightVal, bin.Line, bin.Column);
                case "%":
                    return ModuloValues(leftVal, rightVal, bin.Line, bin.Column);
                case "-":
                    return SubtractValues(leftVal, rightVal, bin.Line, bin.Column);
                default:
                    throw new RuntimeException(
                        $"Unsupported binary operator: {bin.Op}",
                        bin.Line,
                        bin.Column
                    );
            }
        }

        #endregion

        #region Shell Command Execution
        public class ShellResult
        {
            public string Stdout { get; }
            public string Stderr { get; }
            public int ExitCode { get; }
            public long Duration { get; }
            public int Pid { get; }

            public ShellResult(string stdout, string stderr, int exitCode, long duration, int pid)
            {
                Stdout = stdout;
                Stderr = stderr;
                ExitCode = exitCode;
                Duration = duration;
                Pid = pid;
            }

            public Dictionary<string, object?> ParseJson()
            {
                return Stdout.ParseJson();
            }

            public override string ToString() => Stdout.ToString();
        }
        /*
                private ShellResult ExecuteShellCommand(string command, ParserConfig parser, int line, int column)
                {
                    try
                    {
                        // For complex commands, we'll pass them directly to bash without wrapping in quotes
                        // and let ProcessStartInfo handle the argument escaping
                        var psi = new ProcessStartInfo("bash")
                        {
                            Arguments = $"-c \"{EscapeForBashDoubleQuotes(command)}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        };

                        //Console.WriteLine($"executing bash with: {psi.Arguments}");
                        var sw = Stopwatch.StartNew();
                        using var proc = Process.Start(psi)!;
                        int pid = proc.Id;
                        proc.WaitForExit();
                        sw.Stop();
                        string stdout = proc.StandardOutput.ReadToEnd().TrimEnd();
                        string stderr = proc.StandardError.ReadToEnd().TrimEnd();
                        int exitCode = proc.ExitCode;
                        long duration = sw.ElapsedMilliseconds;
                        if (exitCode != 0 && !string.IsNullOrEmpty(stderr))
                        {
                            // Non-zero exit code with error output is a warning, not an error
                            Console.WriteLine(
                                $"[SHELL WARNING] Command exited with code {exitCode}: {stderr}"
                            );
                        }
                        // Return multiple values
                        return new ShellResult(stdout, stderr, exitCode, duration, pid);
                    }
                    catch (Exception e)
                    {
                        string context = _context.GetContextFragment(line);
                        throw new ShellCommandException(e.Message, command, null, line, column, context, e);
                    }
                }*/


        private object? ExecuteShellCommand(string command, ParserConfig parser, int line, int column)
        {
            // Use async version with default timeout for better performance and responsiveness
            try
            {
                var defaultTimeout = TimeSpan.FromSeconds(30); // Default 30 second timeout
                var task = ExecuteShellCommandAsync(command, parser, line, column, CancellationToken.None, defaultTimeout);
                return task.GetAwaiter().GetResult();
            }
            catch (AggregateException ae) when (ae.InnerException != null)
            {
                // Unwrap AggregateException from GetAwaiter().GetResult()
                throw ae.InnerException;
            }
        }

        private async Task<object?> ExecuteShellCommandAsync(string command, ParserConfig parser, int line, int column,
            CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        {
            try
            {
                // Set up the process
                var psi = new ProcessStartInfo("bash")
                {
                    Arguments = $"-c \"{EscapeForBashDoubleQuotes(command)}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                // Create combined cancellation token with timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (timeout.HasValue)
                    cts.CancelAfter(timeout.Value);

                var sw = Stopwatch.StartNew();
                using var proc = Process.Start(psi)!;
                int pid = proc.Id;

                try
                {
                    // Process the output incrementally based on the parser type
                    object? result = null;

                    if (parser is FormatParserConfig formatParser)
                    {
                        // Format parsers need the entire output, so we'll collect it
                        string stdout = await proc.StandardOutput.ReadToEndAsync();
                        result = HandleFormatParser(stdout, formatParser);
                    }
                    else if (parser is FunctionParserConfig funcParser)
                    {
                        // Process line by line as they become available
                        result = await HandleFunctionParserIncrementalAsync(proc.StandardOutput, funcParser, cts.Token);
                    }
                    else if (parser is ObjectParserConfig objParser)
                    {
                        // Process with accumulator
                        result = await HandleObjectParserIncrementalAsync(proc.StandardOutput, objParser, cts.Token);
                    }

                    // Wait for the process to exit with cancellation support
                    await proc.WaitForExitAsync(cts.Token);
                    sw.Stop();

                    string stderr = await proc.StandardError.ReadToEndAsync();
                    stderr = stderr.TrimEnd();
                    int exitCode = proc.ExitCode;
                    long duration = sw.ElapsedMilliseconds;

                    if (exitCode != 0 && !string.IsNullOrEmpty(stderr))
                    {
                        // Non-zero exit code with error output is a warning, not an error
                        Console.WriteLine($"[SHELL WARNING] Command exited with code {exitCode}: {stderr}");
                    }

                    if (result == null)
                    {
                        string stdout = await proc.StandardOutput.ReadToEndAsync();
                        stdout = stdout.TrimEnd();
                        result = new ShellResult(stdout, stderr, exitCode, duration, pid);
                    }

                    return result;
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                    // Kill the process if still running
                    if (!proc.HasExited)
                    {
                        try
                        {
                            proc.Kill(entireProcessTree: true);
                            await proc.WaitForExitAsync(CancellationToken.None);
                        }
                        catch (Exception killEx)
                        {
                            Console.WriteLine($"[SHELL WARNING] Failed to kill process {pid}: {killEx.Message}");
                        }
                    }

                    if (timeout.HasValue && !cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Shell command timed out after {timeout.Value.TotalSeconds} seconds: {command}");
                    }

                    throw; // Re-throw cancellation
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException || e is TimeoutException))
            {
                string context = _context.GetContextFragment(line);
                throw new ShellCommandException(e.Message, command, null, line, column, context, e);
            }
        }

        private object? HandleFormatParser(string output, FormatParserConfig parser)
        {
            switch (parser.Format.ToLowerInvariant())
            {
                case "json":
                    return output.ParseJson();

                case "csv":
                    // Simple CSV parsing logic
                    var lines = output.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
                    if (lines.Count == 0) return new List<Dictionary<string, string>>();

                    var headers = lines[0].Split(',').Select(h => h.Trim()).ToList();
                    var results = new List<Dictionary<string, string>>();

                    for (int i = 1; i < lines.Count; i++)
                    {
                        var values = lines[i].Split(',').Select(v => v.Trim()).ToList();
                        var row = new Dictionary<string, string>();

                        for (int j = 0; j < Math.Min(headers.Count, values.Count); j++)
                        {
                            row[headers[j]] = values[j];
                        }

                        results.Add(row);
                    }

                    return results;

                default:
                    throw new RuntimeException(
                        $"Unsupported format: {parser.Format}",
                        _context.CurrentLocation.Line,
                        _context.CurrentLocation.Column
                    );
            }
        }

        private string HandleFunctionParserIncremental(StreamReader output, FunctionParserConfig parser)
        {
            string result = "";
            // Process output line by line as they become available
            string? line;
            while ((line = output.ReadLine()) != null)
            {
                // Call the line processor function for each line
                _context.PushScope();
                try
                {
                    _context.SetCurrentScopeVariable(parser.LineProcessor.Parameters[0], line);
                    result += EvaluateBlockExpression(parser.LineProcessor.Body);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing line: {ex.Message}");
                }
                finally
                {
                    _context.PopScope();
                }
            }

            return result;
        }

        private object? HandleObjectParserIncremental(StreamReader output, ObjectParserConfig parser)
        {
            // Call the setup function to get initial accumulator
            _context.PushScope();
            object? accumulator;

            try
            {
                // Setup: fn() { return initialValue; }
                try
                {
                    accumulator = EvaluateBlockExpression(parser.Setup.Body);
                }
                catch (ReturnException re)
                {
                    // Handle explicit return from setup
                    accumulator = re.Values.Count > 0 ? re.Values[0] : null;
                }
            }
            finally
            {
                _context.PopScope();
            }

            // Process each line with the line processor as they become available
            string? line;
            bool continueProcessing = true;

            while (continueProcessing && (line = output.ReadLine()) != null)
            {
                _context.PushScope();
                try
                {
                    // Set up parameters for line processor: fn(line, acc) { ... }
                    _context.SetCurrentScopeVariable(parser.LineProcessor.Parameters[0], line);
                    _context.SetCurrentScopeVariable(parser.LineProcessor.Parameters[1], accumulator);

                    // Execute the line processor and update accumulator
                    try
                    {
                        var result = EvaluateBlockExpression(parser.LineProcessor.Body);
                        // A key issue - we need to get the latest value of the accumulator
                        // from the current scope, not just the function return value
                        var updatedAccumulator = _context.LookupVariable(parser.LineProcessor.Parameters[1]);
                        if (updatedAccumulator != null)
                        {
                            accumulator = updatedAccumulator;
                        }
                        // If there's also an explicit return value, use that instead
                        if (result != null)
                        {
                            accumulator = result;
                        }
                    }
                    catch (ReturnException re)
                    {
                        // Handle explicit return
                        if (re.Values.Count > 0)
                        {
                            accumulator = re.Values[0];
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Error handling code remains unchanged
                    if (parser.ErrorHandler != null)
                    {
                        _context.PushScope();
                        try
                        {
                            _context.SetCurrentScopeVariable(parser.ErrorHandler.Parameters[0], ex);
                            _context.SetCurrentScopeVariable(parser.ErrorHandler.Parameters[1], line);

                            // If error handler returns false, abort processing
                            bool shouldContinue = true;
                            try
                            {
                                var result = EvaluateBlockExpression(parser.ErrorHandler.Body);
                                if (result is bool b)
                                {
                                    shouldContinue = b;
                                }
                            }
                            catch (ReturnException re)
                            {
                                if (re.Values.Count > 0 && re.Values[0] is bool b)
                                {
                                    shouldContinue = b;
                                }
                            }

                            if (!shouldContinue)
                            {
                                continueProcessing = false;
                            }
                        }
                        finally
                        {
                            _context.PopScope();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Error processing line: {ex.Message}");
                    }
                }
                finally
                {
                    _context.PopScope();
                }
            }

            // Call the complete function if provided
            if (parser.Complete != null)
            {
                _context.PushScope();
                try
                {
                    _context.SetCurrentScopeVariable(parser.Complete.Parameters[0], accumulator);
                    try
                    {
                        var result = EvaluateBlockExpression(parser.Complete.Body);
                        if (result != null)
                        {
                            accumulator = result;
                        }
                    }
                    catch (ReturnException re)
                    {
                        if (re.Values.Count > 0)
                        {
                            accumulator = re.Values[0];
                        }
                    }
                }
                finally
                {
                    _context.PopScope();
                }
            }

            return accumulator;
        }

        private async Task<string> HandleFunctionParserIncrementalAsync(StreamReader output, FunctionParserConfig parser, CancellationToken cancellationToken)
        {
            string result = "";
            // Process output line by line as they become available
            string? line;
            while ((line = await output.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Call the line processor function for each line
                _context.PushScope();
                try
                {
                    _context.SetCurrentScopeVariable(parser.LineProcessor.Parameters[0], line);
                    result += EvaluateBlockExpression(parser.LineProcessor.Body);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing line: {ex.Message}");
                }
                finally
                {
                    _context.PopScope();
                }
            }

            return result;
        }

        private async Task<object?> HandleObjectParserIncrementalAsync(StreamReader output, ObjectParserConfig parser, CancellationToken cancellationToken)
        {
            // Call the setup function to get initial accumulator
            _context.PushScope();
            object? accumulator;

            try
            {
                // Setup: fn() { return initialValue; }
                try
                {
                    accumulator = EvaluateBlockExpression(parser.Setup.Body);
                }
                catch (ReturnException re)
                {
                    // Handle explicit return from setup
                    accumulator = re.Values.Count > 0 ? re.Values[0] : null;
                }
            }
            finally
            {
                _context.PopScope();
            }

            // Process each line with the line processor as they become available
            string? line;
            bool continueProcessing = true;

            while (continueProcessing && (line = await output.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _context.PushScope();
                try
                {
                    // Set up parameters for line processor: fn(line, acc) { ... }
                    _context.SetCurrentScopeVariable(parser.LineProcessor.Parameters[0], line);
                    _context.SetCurrentScopeVariable(parser.LineProcessor.Parameters[1], accumulator);

                    // Execute the line processor and update accumulator
                    try
                    {
                        var result = EvaluateBlockExpression(parser.LineProcessor.Body);
                        if (result != null)
                        {
                            accumulator = result;
                        }
                    }
                    catch (ReturnException re)
                    {
                        // Handle explicit return
                        if (re.Values.Count > 0)
                        {
                            accumulator = re.Values[0];
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Error handling code remains unchanged
                    if (parser.ErrorHandler != null)
                    {
                        _context.PushScope();
                        try
                        {
                            _context.SetCurrentScopeVariable(parser.ErrorHandler.Parameters[0], ex);
                            _context.SetCurrentScopeVariable(parser.ErrorHandler.Parameters[1], line);

                            // If error handler returns false, abort processing
                            bool shouldContinue = true;
                            try
                            {
                                var result = EvaluateBlockExpression(parser.ErrorHandler.Body);
                                if (result is bool b)
                                {
                                    shouldContinue = b;
                                }
                            }
                            catch (ReturnException re)
                            {
                                if (re.Values.Count > 0 && re.Values[0] is bool b)
                                {
                                    shouldContinue = b;
                                }
                            }

                            if (!shouldContinue)
                            {
                                continueProcessing = false;
                            }
                        }
                        finally
                        {
                            _context.PopScope();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Error processing line: {ex.Message}");
                    }
                }
                finally
                {
                    _context.PopScope();
                }
            }

            // Call the complete function if specified
            if (parser.Complete != null)
            {
                _context.PushScope();
                try
                {
                    _context.SetCurrentScopeVariable(parser.Complete.Parameters[0], accumulator);
                    try
                    {
                        var result = EvaluateBlockExpression(parser.Complete.Body);
                        if (result != null)
                        {
                            accumulator = result;
                        }
                    }
                    catch (ReturnException re)
                    {
                        if (re.Values.Count > 0)
                        {
                            accumulator = re.Values[0];
                        }
                    }
                }
                finally
                {
                    _context.PopScope();
                }
            }

            return accumulator;
        }

        public class SshResult
        {
            public string Stdout { get; }
            public string Stderr { get; }
            public int ExitCode { get; }
            public long Duration { get; }

            public SshResult(string stdout, string stderr, int exitCode, long duration)
            {
                Stdout = stdout;
                Stderr = stderr;
                ExitCode = exitCode;
                Duration = duration;
            }

            public Dictionary<string, object?> ParseJson()
            {
                return Stdout.ParseJson();
            }

            public override string ToString() => Stdout.ToString();
        }

        private object ExecuteSshCommand(SshExpr sshExpr)
        {
            object hostObj = Eval(sshExpr.Host)!;
            object sshCmdObj = Eval(sshExpr.Command)!;
            string host = hostObj?.ToString() ?? "";
            string sshCommand = EscapeForBashDoubleQuotes(sshCmdObj?.ToString() ?? "");


            try
            {
                var sw = Stopwatch.StartNew();
                var proc = Helper.ExecuteSshProc(host, sshCommand);
                // TODO: we changed executessh to executesshproc to return the proc
                // :w

                object? result = null;

                if (sshExpr.Parser is FormatParserConfig formatParser)
                {
                    // Format parsers need the entire output, so we'll collect it
                    string stdout = proc.StandardOutput.ReadToEnd();
                    result = HandleFormatParser(stdout, formatParser);
                }
                else if (sshExpr.Parser is FunctionParserConfig funcParser)
                {
                    // Process line by line as they become available
                    result = HandleFunctionParserIncremental(proc.StandardOutput, funcParser);
                }
                else if (sshExpr.Parser is ObjectParserConfig objParser)
                {
                    // Process with accumulator
                    result = HandleObjectParserIncremental(proc.StandardOutput, objParser);
                }

                sw.Stop();

                // Parse output and errors
                string stderr = "";
                int exitCode = 0;

                /* TODO error handling??
                // Check for error message
                int errorIndex = output.IndexOf("\n[SSH ERROR] ");
                if (errorIndex >= 0)
                {
                    stderr = output.Substring(errorIndex + 13);
                    output = output.Substring(0, errorIndex);
                    exitCode = 1;
                }
                */

                long duration = sw.ElapsedMilliseconds;


                if (result == null)
                {
                    string stdout = proc.StandardOutput.ReadToEnd().TrimEnd();
                    result = new SshResult(stdout, stderr, exitCode, duration);
                }

                return result;

            }
            catch (Exception e)
            {
                int line = sshExpr.Line;
                int column = sshExpr.Column;
                string context = _context.GetContextFragment(line);

                throw new SshCommandException(
                    e.Message,
                    host,
                    sshCommand,
                    null,
                    line,
                    column,
                    context,
                    e
                );
            }
        }

        public static string EscapeForBashDoubleQuotes(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Characters that need special escaping within double quotes in bash
            // - Double quote (") needs to be escaped with backslash
            // - Dollar sign ($) needs to be escaped to prevent variable expansion
            // - Backtick (`) needs to be escaped to prevent command substitution
            // - Backslash (\) needs to be escaped in certain contexts
            // - Exclamation mark (!) might need escaping in some contexts for history expansion

            StringBuilder result = new StringBuilder(input.Length * 2);

            foreach (char c in input)
            {
                switch (c)
                {
                    case '"':
                        result.Append("\\\"");
                        break;
                    case '$':
                        result.Append("$");
                        break;
                    case '`':
                        result.Append("`");
                        break;
                    case '\\':
                        result.Append("\\\\");
                        break;
                    case '!':
                        result.Append("!");
                        break;
                    default:
                        result.Append(c);
                        break;
                }
            }

            return result.ToString();
        }
        #endregion

        #region Utility Methods

        // A cache to avoid repeated reflection
        private static readonly ConcurrentDictionary<(Type, string), List<MethodInfo>> _extMethodCache =
            new ConcurrentDictionary<(Type, string), List<MethodInfo>>();

        // Compiled member access cache for better performance
        private static readonly ConcurrentDictionary<(Type, string), CompiledMemberAccess> _memberCache =
            new ConcurrentDictionary<(Type, string), CompiledMemberAccess>();

        public enum MemberType
        {
            None,
            Property,
            Field,
            Method,
            ExtensionMethod
        }

        public readonly struct CompiledMemberAccess
        {
            public readonly MemberType Type;
            public readonly Func<object, object>? CompiledProperty;
            public readonly Func<object, object?, object>? CompiledField;
            public readonly List<MethodInfo>? Methods;
            public readonly List<MethodInfo>? ExtensionMethods;
            public readonly object? Parent;

            public CompiledMemberAccess(MemberType type, Func<object, object>? compiledProperty = null,
                Func<object, object?, object>? compiledField = null, List<MethodInfo>? methods = null,
                List<MethodInfo>? extensionMethods = null, object? parent = null)
            {
                Type = type;
                CompiledProperty = compiledProperty;
                CompiledField = compiledField;
                Methods = methods;
                ExtensionMethods = extensionMethods;
                Parent = parent;
            }
        }

        private CompiledMemberAccess CompileMemberAccess(Type type, string memberName)
        {
            try
            {
                // First try to find instance methods
                var instanceMethods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (instanceMethods.Any())
                {
                    return new CompiledMemberAccess(MemberType.Method, methods: instanceMethods);
                }

                // Try extension methods
                var extMethods = FindExtensionMethods(type, memberName);
                if (extMethods.Any())
                {
                    return new CompiledMemberAccess(MemberType.ExtensionMethod, extensionMethods: extMethods);
                }

                // Try properties
                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (property != null)
                {
                    try
                    {
                        // Compile property access using Expression trees
                        var parameter = Expression.Parameter(typeof(object), "obj");
                        var castParameter = Expression.Convert(parameter, type);
                        var propertyAccess = Expression.Property(castParameter, property);
                        var castResult = Expression.Convert(propertyAccess, typeof(object));
                        var lambda = Expression.Lambda<Func<object, object>>(castResult, parameter);
                        var compiledProperty = lambda.Compile();

                        return new CompiledMemberAccess(MemberType.Property, compiledProperty: compiledProperty);
                    }
                    catch
                    {
                        // Fallback to reflection if compilation fails
                        return new CompiledMemberAccess(MemberType.Property);
                    }
                }

                // Try fields
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    try
                    {
                        // Compile field access using Expression trees
                        var parameter = Expression.Parameter(typeof(object), "obj");
                        var castParameter = Expression.Convert(parameter, type);
                        var fieldAccess = Expression.Field(castParameter, field);
                        var castResult = Expression.Convert(fieldAccess, typeof(object));
                        var lambda = Expression.Lambda<Func<object, object>>(castResult, parameter);
                        var compiledField = lambda.Compile();

                        return new CompiledMemberAccess(MemberType.Field, compiledProperty: compiledField);
                    }
                    catch
                    {
                        // Fallback to reflection if compilation fails
                        return new CompiledMemberAccess(MemberType.Field);
                    }
                }

                return new CompiledMemberAccess(MemberType.None);
            }
            catch
            {
                // If anything fails, return None type
                return new CompiledMemberAccess(MemberType.None);
            }
        }

        private List<MethodInfo> FindExtensionMethods(Type targetType, string methodName)
        {
            var key = (targetType, methodName);
            return _extMethodCache.GetOrAdd(key, k =>
            {
                var methods = new List<MethodInfo>();

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        // Only consider static classes
                        if (!type.IsAbstract || !type.IsSealed)
                            continue;

                        foreach (
                            var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public)
                        )
                        {
                            if (
                                !string.Equals(
                                    method.Name,
                                    methodName,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                                continue;

                            var parameters = method.GetParameters();
                            if (parameters.Length == 0)
                                continue;

                            if (parameters[0].ParameterType.IsAssignableFrom(targetType))
                            {
                                methods.Add(method);
                            }
                        }
                    }
                }

                return methods;
            });
        }

        // Comparison operators
        private bool CompareEqual(object left, object right)
        {
            if (left == null && right == null)
                return true;
            if (left == null || right == null)
                return false;

            // If both are integers
            if (left is int li && right is int ri)
                return li == ri;

            // Default to string comparison for other types
            return left.ToString() == right.ToString();
        }

        private bool CompareNotEqual(object left, object right)
        {
            return !CompareEqual(left, right);
        }

        private bool CompareGreater(object left, object right)
        {
            int l = ConvertToInt(left);
            int r = ConvertToInt(right);
            return l > r;
        }

        private bool CompareGreaterEqual(object left, object right)
        {
            int l = ConvertToInt(left);
            int r = ConvertToInt(right);
            return l >= r;
        }

        private bool CompareLessEqual(object left, object right)
        {
            int l = ConvertToInt(left);
            int r = ConvertToInt(right);
            return l <= r;
        }

        private bool CompareLess(object left, object right)
        {
            int l = ConvertToInt(left);
            int r = ConvertToInt(right);
            return l < r;
        }

        // Arithmetic operators
        private object AddValues(object left, object right)
        {
            // If both are numbers, sum them
            if (left is int li && right is int ri)
            {
                return li + ri;
            }
            // Else treat as strings
            return (left?.ToString() ?? "") + (right?.ToString() ?? "");
        }

        private object MultiplyValues(object left, object right, int line = 0, int column = 0)
        {
            if (left is int li && right is int ri)
            {
                return li * ri;
            }
            var context = CreateErrorContext(line, column);
            throw context.CreateRuntimeException($"Cannot multiply {left} and {right} - expected numeric values");
        }

        private object DivideValues(object left, object right, int line = 0, int column = 0)
        {
            if (left is int li && right is int ri)
            {
                if (ri == 0)
                {
                    var divZeroContext = CreateErrorContext(line, column);
                    throw divZeroContext.CreateRuntimeException("Division by zero");
                }
                return li / ri;
            }
            var typeErrorContext = CreateErrorContext(line, column);
            throw typeErrorContext.CreateRuntimeException($"Cannot divide {left} and {right} - expected numeric values");
        }

        private object ModuloValues(object left, object right, int line = 0, int column = 0)
        {
            if (left is int li && right is int ri)
            {
                if (ri == 0)
                {
                    var modZeroContext = CreateErrorContext(line, column);
                    throw modZeroContext.CreateRuntimeException("Modulo by zero");
                }
                return li % ri;
            }
            var typeErrorContext = CreateErrorContext(line, column);
            throw typeErrorContext.CreateRuntimeException($"Cannot calculate modulo of {left} and {right} - expected numeric values");
        }

        private object SubtractValues(object left, object right, int line = 0, int column = 0)
        {
            if (left is int li && right is int ri)
            {
                return li - ri;
            }
            var context = CreateErrorContext(line, column);
            throw context.CreateRuntimeException($"Cannot subtract {left} and {right} - expected numeric values");
        }

        // Type conversion helpers
        private int ConvertToInt(object val)
        {
            if (val is int i)
                return i;
            if (val is bool b)
                return b ? 1 : 0;

            int.TryParse(val?.ToString() ?? "0", out int result);
            return result;
        }

        private bool ConvertToBool(object val)
        {
            if (val is bool b)
                return b;
            if (val is int i)
                return i != 0;
            if (val is string s)
                return !string.IsNullOrEmpty(s);

            return val != null;
        }

        #endregion

        #region Expression Visitor Methods

        public object? Visit(LiteralExpr expr)
        {
            return expr.Value;
        }

        public object? Visit(InterpolatedStringExpr expr)
        {
            // Estimate capacity to reduce StringBuilder reallocations
            int estimatedCapacity = EstimateInterpolatedStringCapacity(expr.Parts);
            var sb = new StringBuilder(estimatedCapacity);
            
            foreach (var part in expr.Parts)
            {
                object? value = Eval(part);
                sb.Append(value?.ToString() ?? "");
            }
            return sb.ToString();
        }

        private static int EstimateInterpolatedStringCapacity(List<Expr> parts)
        {
            int capacity = 0;
            foreach (var part in parts)
            {
                if (part is LiteralExpr literal && literal.Value is string str)
                {
                    // String literals - we know the exact length
                    capacity += str.Length;
                }
                else
                {
                    // Expression parts - estimate based on common types
                    // Numbers: ~10 chars, booleans: ~5 chars, other: ~20 chars average
                    capacity += 20;
                }
            }
            
            // Add 25% buffer for safety, minimum 16 chars
            return Math.Max(16, (int)(capacity * 1.25));
        }

        public object? Visit(VarExpr expr)
        {
            return _context.LookupVariable(expr.Name);
        }

        public object? Visit(CallExpr expr)
        {
            object? callee = Eval(expr.Callee);
            if (callee is Callable callable)
            {
                List<object?> args = new();
                foreach (var arg in expr.Arguments)
                    args.Add(Eval(arg));
                return callable.Call(this, args);
            }
            throw new RuntimeException(
                $"Attempted to call a non-function '{callee}'",
                expr.Line,
                expr.Column
            );
        }

        public object? Visit(BinaryExpr expr)
        {
            return EvalBinary(expr);
        }

        public object? Visit(IfExpr expr)
        {
            var condVal = Eval(expr.Condition)!;
            bool condition = ConvertToBool(condVal);
            if (condition)
                return EvaluateBlockExpression(expr.ThenBlock);
            else if (expr.ElseBlock != null)
                return EvaluateBlockExpression(expr.ElseBlock);
            else
                return null;
        }

        public object? Visit(ForExpr expr)
        {
            var iterable = Eval(expr.Iterable);
            if (!(iterable is IEnumerable en))
                throw new RuntimeException(
                    "For expression must iterate over an enumerable",
                    expr.Line,
                    expr.Column
                );

            var results = new List<object?>();
            foreach (object item in en)
            {
                _context.PushScope();
                _context.SetCurrentScopeVariable(expr.IteratorVar, item);

                try
                {
                    var value = EvaluateBlockExpression(expr.Body);
                    results.Add(value);
                }
                finally
                {
                    _context.PopScope();
                }
            }
            return results;
        }

        public object? Visit(ParallelForExpr expr)
        {
            return EvalParallelFor(expr);
        }

        public object? Visit(LambdaExpr expr)
        {
            // capture current variables
            var closure = _context.GetAllVariables();

            var lambdaDecl = new FunctionStmt("<lambda>", expr.Parameters, expr.Body)
            {
                Line = expr.Line,
                Column = expr.Column,
            };

            return new Function(lambdaDecl, closure);
        }

        public object? Visit(ShellExpr expr)
        {
            object cmdObj = Eval(expr.Argument)!;
            string command;

            // If it's already a string, use it directly
            if (cmdObj is string cmdStr)
            {
                command = cmdStr;
            }
            else
            {
                // Otherwise convert to string
                command = cmdObj?.ToString() ?? "";
            }

            return ExecuteShellCommand(command, expr.Parser, expr.Line, expr.Column);
        }

        public object? Visit(SshExpr expr)
        {
            return ExecuteSshCommand(expr);
        }

        public object? Visit(MemberAccessExpr expr)
        {
#if PERF_TRACKING
            var memberSw = System.Diagnostics.Stopwatch.StartNew();
            var evalSw = System.Diagnostics.Stopwatch.StartNew();
#endif
            object parent = Eval(expr.Object)!;
#if PERF_TRACKING
            evalSw.Stop();
            
            var bindSw = System.Diagnostics.Stopwatch.StartNew();
#endif
            var result = BindMember(parent, expr.MemberName);
#if PERF_TRACKING
            bindSw.Stop();
            memberSw.Stop();
            
            if (memberSw.ElapsedMilliseconds > 5)
            {
                Console.WriteLine($"[PERF] Slow member access '{parent?.GetType().Name}.{expr.MemberName}': total={memberSw.ElapsedMilliseconds}ms (eval={evalSw.ElapsedMilliseconds}ms, bind={bindSw.ElapsedMilliseconds}ms)");
            }
#endif
            
            return result;
        }

        public object? Visit(RangeExpr expr)
        {
            object leftVal = Eval(expr.Start)!;
            object rightVal = Eval(expr.End)!;
            int start = ConvertToInt(leftVal)!;
            int end = ConvertToInt(rightVal)!;
            return new Range(start, end);
        }

        public object? Visit(ArrayExpr expr)
        {
            var list = new List<object?>();
            foreach (var element in expr.Elements)
                list.Add(Eval(element));
            return list;
        }

        public object? Visit(IndexExpr expr)
        {
            object target = Eval(expr.Target)!;
            object indexValue = Eval(expr.Index)!;

            if (target is IList<object> tlist)
            {
                int i = ConvertToInt(indexValue);
                if (i < 0 || i >= tlist.Count)
                    throw new RuntimeException(
                        $"Index {i} is out of range for list of length {tlist.Count}",
                        expr.Line,
                        expr.Column
                    );
                return tlist[i];
            }
            else if (target is IDictionary<string, object> tdict)
            {
                string key = indexValue?.ToString() ?? "";
                if (!tdict.ContainsKey(key))
                    throw new RuntimeException(
                        $"Key '{key}' not found in dictionary",
                        expr.Line,
                        expr.Column
                    );
                return tdict[key];
            }
            else
            {
                throw new RuntimeException(
                    $"Type {target?.GetType().Name ?? "null"} does not support indexing",
                    expr.Line,
                    expr.Column
                );
            }
        }

        public object? Visit(DictExpr expr)
        {
            var map = new Dictionary<string, object?>();
            foreach (var (keyExpr, valueExpr) in expr.Pairs)
            {
                var keyObj = Eval(keyExpr);
                if (keyObj == null)
                    throw new RuntimeException(
                        "Dictionary key cannot be null",
                        expr.Line,
                        expr.Column
                    );

                string key = keyObj.ToString()!;
                map[key] = Eval(valueExpr);
            }
            return map;
        }

        public object? Visit(TimeLiteralExpr expr)
        {
            // Return the TimeLiteralExpr itself so it can be used in variable assignments
            // and also provide the milliseconds value for calculations
            return expr;
        }

        #endregion

        #region Error Context Methods

        /// <summary>
        /// Creates a rich error context for the current execution state
        /// </summary>
        public ShelltracErrorContext CreateErrorContext(int line = 0, int column = 0, string sourceSnippet = "")
        {
            var variables = new Dictionary<string, object?>();
            
            // Capture all variables from all scopes for debugging
            try
            {
                var allVariables = _context.GetAllVariables();
                foreach (var (name, value) in allVariables.Take(20)) // Limit to prevent excessive output
                {
                    variables[name] = value;
                }
            }
            catch
            {
                // Ignore errors when capturing variables
            }

            return new ShelltracErrorContext(
                fileName: _context.CurrentLocation.ScriptName ?? "",
                line: line > 0 ? line : _context.CurrentLocation.Line,
                column: column > 0 ? column : _context.CurrentLocation.Column,
                sourceSnippet: !string.IsNullOrEmpty(sourceSnippet) ? sourceSnippet : GetSourceSnippet(line > 0 ? line : _context.CurrentLocation.Line),
                variables: variables
            );
        }

        /// <summary>
        /// Gets verbose source snippet around the specified line for error reporting
        /// </summary>
        private string GetSourceSnippet(int line)
        {
            if (string.IsNullOrEmpty(_sourceCode) || line <= 0)
                return "";

            try
            {
                var lines = _sourceCode.Split('\n');
                if (line > lines.Length)
                    return "";

                // Show extensive context: 3 lines before, the error line, and 3 lines after
                var contextLines = 3;
                var startLine = Math.Max(0, line - contextLines - 1);
                var endLine = Math.Min(lines.Length - 1, line + contextLines - 1);
                
                var snippet = new StringBuilder();
                snippet.AppendLine();
                snippet.AppendLine("┌─ Source Code Context ─────────────────────────────────────────────┐");
                
                for (int i = startLine; i <= endLine; i++)
                {
                    bool isErrorLine = (i == line - 1);
                    string marker = isErrorLine ? "ERROR→" : "      ";
                    string lineNum = $"{i + 1}".PadLeft(4);
                    string codeLine = lines[i];
                    
                    if (isErrorLine)
                    {
                        snippet.AppendLine($"│ {marker} {lineNum}: {codeLine}");
                        
                        // Add column indicator if we have column information
                        var column = _context.CurrentLocation.Column;
                        if (column > 0 && column <= codeLine.Length + 1)
                        {
                            var spaces = new string(' ', column - 1);
                            var indicator = "^".PadLeft(Math.Max(1, codeLine.Length - column + 2), '~');
                            snippet.AppendLine($"│              {spaces}{indicator}");
                        }
                    }
                    else
                    {
                        snippet.AppendLine($"│ {marker} {lineNum}: {codeLine}");
                    }
                }
                
                snippet.AppendLine("└───────────────────────────────────────────────────────────────────┘");
                
                return snippet.ToString();
            }
            catch
            {
                return "";
            }
        }

        #endregion
    }

    public class ReturnException : Exception
    {
        public List<object?> Values { get; }

        public ReturnException(List<object?> values) => Values = values;

        public ReturnException(object? value)
            : this(new List<object?> { value }) { }
    }

    public class YieldException : Exception
    {
        public object? Value { get; }
        public bool IsEmit { get; }
        public bool IsGlobalCancel { get; }
        public bool IsOverride { get; }

        public YieldException(object? value, bool isEmit, bool isGlobalCancel, bool isOverride)
        {
            Value = value;
            IsEmit = isEmit;
            IsGlobalCancel = isGlobalCancel;
            IsOverride = isOverride;
        }
    }

    public interface Callable
    {
        object? Call(Executor executor, List<object?> arguments);
    }

    public class Function : Callable
    {
        public FunctionStmt Declaration { get; }
        public ImmutableDictionary<string, object?> Closure { get; } // capture environment if needed
        
        // Cache attribute analysis to avoid checking on every call
        private readonly bool _isCached;
        private readonly long _cacheTtlMilliseconds;

        public Function(FunctionStmt declaration, ImmutableDictionary<string, object?> closure)
        {
            Declaration = declaration;
            Closure = closure;
            
            // Pre-compute caching information to avoid runtime overhead
            var cacheAttribute = declaration.Attributes.FirstOrDefault(a => string.Equals(a.Name, "cache", StringComparison.OrdinalIgnoreCase));
            if (cacheAttribute != null)
            {
                _isCached = true;
                _cacheTtlMilliseconds = GetCacheTtl(cacheAttribute);
            }
            else
            {
                _isCached = false;
                _cacheTtlMilliseconds = 0;
            }
        }

        public object? Call(Executor executor, List<object?> arguments)
        {
            // Fast path for non-cached functions
            if (!_isCached)
            {
                return ExecuteFunction(executor, arguments);
            }
            
            // Cached function path
            string cacheKey = FunctionCache.GenerateKey(Declaration.Name, arguments);
            
            // Try to get cached result
            if (FunctionCache.TryGet(cacheKey, out object? cachedValue))
            {
                return cachedValue;
            }
            
            // Execute function and cache result
            object? result = ExecuteFunction(executor, arguments);
            FunctionCache.Set(cacheKey, result, _cacheTtlMilliseconds);
            
            return result;
        }

        private object? ExecuteFunction(Executor executor, List<object?> arguments)
        {
            // Create new environment from immutable closure
            var localEnv = Closure.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            for (int i = 0; i < Declaration.Parameters.Count; i++)
            {
                localEnv[Declaration.Parameters[i]] = i < arguments.Count ? arguments[i] : null;
            }
            executor.PushEnvironment(localEnv);

            object? returnValue = null;
            try
            {
                executor.ExecuteBlock(Declaration.Body);
            }
            catch (ReturnException ret)
            {
                // If we have exactly one value, return it directly
                // Otherwise, return the list of values
                returnValue = ret.Values.Count == 1 ? ret.Values[0] : ret.Values;
            }
            finally
            {
                executor.PopEnvironment();
            }
            return returnValue;
        }

        private static long GetCacheTtl(FunctionAttribute cacheAttribute)
        {
            // Default TTL is 5 minutes if not specified
            const long defaultTtl = 5 * 60 * 1000; // 5 minutes in milliseconds
            
            if (cacheAttribute.Parameters.TryGetValue("ttl", out object? ttlValue))
            {
                if (ttlValue is TimeLiteralExpr timeLiteral)
                {
                    return timeLiteral.TotalMilliseconds;
                }
                else if (ttlValue is int intValue)
                {
                    return intValue; // Assume milliseconds
                }
            }
            
            return defaultTtl;
        }
    }

    public class Range : IEnumerable<int>
    {
        public int Start { get; }
        public int End { get; }

        public Range(int start, int end)
        {
            Start = start;
            End = end;
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (int i = Start; i <= End; i++)
            {
                yield return i;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"Range({Start}..{End})";
    }

    public class EventHandler
    {
        public List<string> Parameters { get; }
        public List<Stmt> Body { get; }

        public EventHandler(List<string> parameters, List<Stmt> body)
        {
            Parameters = parameters;
            Body = body;
        }
    }

    public class ReturnTuple
    {
        public List<object?> Values { get; }

        public ReturnTuple(List<object?> values) => Values = values;
    }

    public class ExtensionMethodCallable : Callable
    {
        private readonly object _target;
        private readonly List<MethodInfo> _methods;

        public ExtensionMethodCallable(object target, List<MethodInfo> methods)
        {
            _target = target;
            _methods = methods;
        }

        public object? Call(Executor executor, List<object?> arguments)
        {
            // Choose an overload that takes arguments.Count + 1 parameters
            MethodInfo? method = _methods.FirstOrDefault(m =>
                m.GetParameters().Length == arguments.Count + 1
            );

            if (method == null)
                throw new RuntimeException(
                    $"No extension method found for {_target.GetType().Name} with {arguments.Count} arguments",
                    executor.Context.CurrentLocation.Line,
                    executor.Context.CurrentLocation.Column
                );

            ParameterInfo[] parameters = method.GetParameters();
            object?[] convertedArgs = new object?[arguments.Count + 1];
            convertedArgs[0] = _target; // Inject the target

            for (int i = 0; i < arguments.Count; i++)
            {
                try
                {
                    convertedArgs[i + 1] = Convert.ChangeType(
                        arguments[i],
                        parameters[i + 1].ParameterType
                    );
                }
                catch (Exception ex)
                {
                    throw new RuntimeException(
                        $"Cannot convert argument {i + 1} from {arguments[i]?.GetType().Name ?? "null"} to {parameters[i + 1].ParameterType.Name}",
                        executor.Context.CurrentLocation.Line,
                        executor.Context.CurrentLocation.Column,
                        innerException: ex
                    );
                }
            }

            try
            {
                object? result = method.Invoke(null, convertedArgs);

                // If the result is already a collection that represents multiple values, return it directly
                if (result is IEnumerable<object> objCollection && !(result is string))
                {
                    return objCollection.ToList();
                }

                return result;
            }
            catch (TargetInvocationException tie)
            {
                throw new RuntimeException(
                    "Extension method invocation failed: " + tie.InnerException?.Message,
                    executor.Context.CurrentLocation.Line,
                    executor.Context.CurrentLocation.Column,
                    innerException: tie.InnerException
                );
            }
        }
    }

    public class UFCSCallable : Callable
    {
        private readonly object _target;
        private readonly Callable _function;

        public UFCSCallable(object target, Callable function)
        {
            _target = target;
            _function = function;
        }

        public object? Call(Executor executor, List<object?> arguments)
        {
            // Create new argument list with target as first parameter
            var ufcsArgs = new List<object?> { _target };
            ufcsArgs.AddRange(arguments);
            
            // Call the underlying function with target as first argument
            return _function.Call(executor, ufcsArgs);
        }
    }
}
