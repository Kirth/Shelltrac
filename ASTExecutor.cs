using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection; // just for BindingFlags
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shelltrac
{
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

            _context.SetCurrentScopeVariable("read_file", new BuiltinFunction(
                "read_file",
                (exec, args) =>
                {
                    if (args.Count != 1)
                        throw new RuntimeException(
                            "read_file() expects exactly one argument (file path).",
                            _context.CurrentLocation.Line,
                            _context.CurrentLocation.Column
                        );

                    string filePath = args[0]?.ToString() ?? "";

                    try
                    {
                        if (!File.Exists(filePath))
                            throw new RuntimeException(
                                $"File not found: {filePath}",
                                _context.CurrentLocation.Line,
                                _context.CurrentLocation.Column
                            );

                        return File.ReadAllText(filePath);
                    }
                    catch (Exception ex) when (!(ex is RuntimeException))
                    {
                        throw new RuntimeException(
                            $"Error reading file: {ex.Message}",
                            _context.CurrentLocation.Line,
                            _context.CurrentLocation.Column,
                            _context.GetContextFragment(_context.CurrentLocation.Line),
                            ex
                        );
                    }
                }
            ));

            // Register built-in functions in the global scope
            _context.SetCurrentScopeVariable("wait", new BuiltinFunction(
                "wait",
                (exec, args) =>
                {
                    if (args.Count != 1)
                        throw new RuntimeException(
                            "wait() expects exactly one argument (milliseconds).",
                            _context.CurrentLocation.Line,
                            _context.CurrentLocation.Column
                        );

                    int ms = Convert.ToInt32(args[0]);
                    System.Threading.Thread.Sleep(ms);
                    return null;
                }
            ));

            _context.SetCurrentScopeVariable("instantiate", new BuiltinFunction(
                "instantiate",
                (exec, args) =>
                {
                    if (args.Count < 1 || args.Count > 2)
                        throw new RuntimeException(
                            "instantiate(typeName[, ctorArgs]) expects 1 or 2 args",
                            exec.Context.CurrentLocation.Line,
                            exec.Context.CurrentLocation.Column
                        );

                    string typeName = args[0]?.ToString() ?? "";
                    Type? type = Type.GetType(typeName);

                    // search loaded assemblies for the type
                    if (type == null)
                    {
                        type = AppDomain
                            .CurrentDomain.GetAssemblies()
                            .Select(a => a.GetType(typeName))
                            .FirstOrDefault(t => t != null);
                    }

                    // try loading the assembly based on namespace (before last dot)
                    if (type == null && typeName.Contains("."))
                    {
                        var asmName = typeName.Substring(0, typeName.LastIndexOf('.'));
                        try
                        {
                            var asm = Assembly.Load(asmName);
                            type = asm.GetType(typeName);
                        }
                        catch
                        { /* swallow load errors */
                        }
                    }

                    if (type == null)
                        throw new RuntimeException(
                            $"Type to instantiate '{typeName}' not found",
                            exec.Context.CurrentLocation.Line,
                            exec.Context.CurrentLocation.Column
                        );

                    // handle ctor args
                    object?[] ctorArgs = Array.Empty<object?>();
                    if (args.Count == 2)
                    {
                        if (args[1] is List<object?> list)
                            ctorArgs = list.ToArray();
                        else
                            throw new RuntimeException(
                                "Second argument to `instantiate` must be an array",
                                exec.Context.CurrentLocation.Line,
                                exec.Context.CurrentLocation.Column
                            );
                    }

                    // pick a ctor matching arg count
                    var ctor =
                        type.GetConstructors()
                            .FirstOrDefault(c => c.GetParameters().Length == ctorArgs.Length)
                        ?? throw new RuntimeException(
                            $"No constructor on '{typeName}' with {ctorArgs.Length} args",
                            exec.Context.CurrentLocation.Line,
                            exec.Context.CurrentLocation.Column
                        );

                    // convert & invoke
                    var pars = ctor.GetParameters();
                    var converted = new object?[ctorArgs.Length];
                    for (int i = 0; i < ctorArgs.Length; i++)
                        converted[i] = Convert.ChangeType(ctorArgs[i], pars[i].ParameterType);

                    return ctor.Invoke(converted);
                }
            ));
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
            // Lists for once-tasks and fire-and-forget every-tasks
            var onceTasks = new List<Task>();

            // Process statements in order
            foreach (var stmt in program.Statements)
            {
                try
                {
                    if (stmt is TaskStmt task)
                    {
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
                        if (!_eventHandlers.ContainsKey(ev.EventName))
                            _eventHandlers[ev.EventName] = new List<EventHandler>();

                        _eventHandlers[ev.EventName].Add(new EventHandler(ev.Parameters, ev.Body));
                    }
                    else
                    {
                        // For all other statements, execute them immediately
                        ExecuteStmt(stmt);
                    }
                }
                catch (ShelltracException ex)
                {
                    Console.Error.WriteLine($"[ERROR] {ex}");
                    // Continue execution despite errors in top-level statements
                }
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
            // Evaluate the argument expression
            object val = Eval(inv.Argument)!;

            switch (inv.CommandKeyword)
            {
                case "log":
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {Helper.ToPrettyString(val)}");
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
                throw new RuntimeException(
                    $"Variable '{stmt.VarName}' not found.",
                    stmt.Line,
                    stmt.Column,
                    _context.GetContextFragment(stmt.Line)
                );
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

            var type = parent.GetType();

            // First, look for instance methods
            var instanceMethods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (instanceMethods.Any())
            {
                return new ReflectionCallable(parent, instanceMethods);
            }

            // Generalized extension search
            var extMethods = FindExtensionMethods(type, memberName);
            if (extMethods.Any())
            {
                return new ExtensionMethodCallable(parent, extMethods);
            }

            // Fall back to properties
            var property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
            );

            if (property != null)
            {
                return property.GetValue(parent);
            }

            throw new RuntimeException(
                $"Member '{memberName}' not found on object of type {type.Name}",
                _context.CurrentLocation.Line,
                _context.CurrentLocation.Column
            );
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
                    return MultiplyValues(leftVal, rightVal);
                case "/":
                    return DivideValues(leftVal, rightVal);
                case "-":
                    return SubtractValues(leftVal, rightVal);
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

        private object MultiplyValues(object left, object right)
        {
            if (left is int li && right is int ri)
            {
                return li * ri;
            }
            throw new RuntimeException(
                $"Cannot multiply {left} and {right} - expected numeric values",
                _context.CurrentLocation.Line,
                _context.CurrentLocation.Column
            );
        }

        private object DivideValues(object left, object right)
        {
            if (left is int li && right is int ri)
            {
                if (ri == 0)
                    throw new RuntimeException(
                        "Division by zero",
                        _context.CurrentLocation.Line,
                        _context.CurrentLocation.Column
                    );
                return li / ri;
            }
            throw new RuntimeException(
                $"Cannot divide {left} and {right} - expected numeric values",
                _context.CurrentLocation.Line,
                _context.CurrentLocation.Column,
                _context.GetContextFragment(_context.CurrentLocation.Line)
            );
        }

        private object SubtractValues(object left, object right)
        {
            if (left is int li && right is int ri)
            {
                return li - ri;
            }
            throw new RuntimeException(
                $"Cannot subtract {left} and {right} - expected numeric values",
                _context.CurrentLocation.Line,
                _context.CurrentLocation.Column,
                _context.GetContextFragment(_context.CurrentLocation.Line)
            );
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
            var sb = new StringBuilder();
            foreach (var part in expr.Parts)
            {
                object? value = Eval(part);
                sb.Append(value?.ToString() ?? "");
            }
            return sb.ToString();
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
            object parent = Eval(expr.Object)!;
            return BindMember(parent, expr.MemberName);
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

    public class BuiltinFunction : Callable
    {
        private readonly Func<Executor, List<object?>, object?> _func;
        public string Name { get; }

        public BuiltinFunction(string name, Func<Executor, List<object?>, object?> func)
        {
            Name = name;
            _func = func;
        }

        public object? Call(Executor executor, List<object?> arguments)
        {
            return _func(executor, arguments);
        }

        public override string ToString() => $"[builtin fn {Name}]";
    }

    public class Function : Callable
    {
        public FunctionStmt Declaration { get; }
        public ImmutableDictionary<string, object?> Closure { get; } // capture environment if needed

        public Function(FunctionStmt declaration, ImmutableDictionary<string, object?> closure)
        {
            Declaration = declaration;
            Closure = closure;
        }

        public object? Call(Executor executor, List<object?> arguments)
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
}
