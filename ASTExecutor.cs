using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection; // just for BindingFlags
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shelltrac
{
    public class ExecutionScope
    {
        // Variable storage
        public Dictionary<string, object?> Variables { get; } = new Dictionary<string, object?>();

        // Execution tracking
        public SourceLocation Location { get; set; }

        public ExecutionScope(
            SourceLocation location,
            Dictionary<string, object?>? parentVariables = null
        )
        {
            Location = location;

            // Optionally inherit variables from parent scope
            if (parentVariables != null)
            {
                foreach (var kvp in parentVariables)
                {
                    Variables[kvp.Key] = kvp.Value;
                }
            }
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

            var parentVars = inheritVariables ? CurrentScope.Variables : null;
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
        public Dictionary<string, object?> GetAllVariables()
        {
            var result = new Dictionary<string, object?>();

            // Start with globals and override with more local scopes
            for (int i = 0; i < _scopeStack.Count; i++)
            {
                foreach (var kvp in _scopeStack[i].Variables)
                {
                    result[kvp.Key] = kvp.Value;
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
                    _scopeStack[i].Variables[name] = value;
                    return true;
                }
            }
            return false;
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

    public class Executor
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
        public Executor(string scriptPath = null, string sourceCode = null)
        {
            _scriptPath = scriptPath ?? "script";
            _sourceCode = sourceCode ?? "";
            _context = new ExecutionContext(_scriptPath, _sourceCode);

            // Register built-in functions in the global scope
            _context.GlobalScope.Variables["wait"] = new BuiltinFunction(
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
            );

            _context.GlobalScope.Variables["instantiate"] = new BuiltinFunction(
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
            );
            ;
        }

        public void PushEnvironment(Dictionary<string, object?> env)
        {
            _context.PushScope();
            foreach (var kvp in env)
            {
                _context.CurrentScope.Variables[kvp.Key] = kvp.Value;
            }
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

                switch (stmt)
                {
                    case TaskStmt:
                    case EventStmt:
                        // No-op here, handled in Execute
                        break;

                    case TriggerStmt trigger:
                        HandleTrigger(trigger);
                        break;

                    case InvocationStmt inv:
                        HandleInvocation(inv);
                        break;

                    case VarDeclStmt vd:
                        // Always put in current scope
                        object val = Eval(vd.Initializer);
                        _context.CurrentScope.Variables[vd.VarName] = val;
                        break;

                    case AssignStmt assignStmt:
                        object rhsVal = Eval(assignStmt.ValueExpr);
                        if (!_context.AssignVariable(assignStmt.VarName, rhsVal))
                        {
                            throw new RuntimeException(
                                $"Variable '{assignStmt.VarName}' not found.",
                                stmt.Line,
                                stmt.Column,
                                _context.GetContextFragment(stmt.Line)
                            );
                        }
                        break;

                    case IndexAssignStmt indexAssign:
                        HandleIndexAssign(indexAssign);
                        break;

                    case IfStmt ifs:
                        HandleIf(ifs);
                        break;

                    case ForStmt fs:
                        HandleFor(fs);
                        break;

                    case FunctionStmt func:
                        // Store function in the current scope
                        _context.CurrentScope.Variables[func.Name] = new Function(
                            func,
                            new Dictionary<string, object>(_context.CurrentScope.Variables)
                        );
                        break;

                    case ReturnStmt ret:
                        List<object?> returnValues = new List<object?>();
                        foreach (var valueExpr in ret.Values)
                        {
                            returnValues.Add(Eval(valueExpr));
                        }
                        throw new ReturnException(returnValues);

                    case DestructuringAssignStmt destructAssign:
                        HandleDestructuringAssign(destructAssign);
                        break;

                    case LoopYieldStmt yield:
                        object? yieldValue = yield.Value != null ? Eval(yield.Value) : null;
                        throw new YieldException(
                            yieldValue,
                            yield.IsEmit,
                            yield.IsGlobalCancel,
                            yield.IsOverride
                        );

                    case ExpressionStmt expr:
                        Eval(expr.Expression);
                        break;
                }
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
                            _context.CurrentScope.Variables[handler.Parameters[i]] = evaluatedArgs[
                                i
                            ];
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
            object val = Eval(inv.Argument);

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
                    ExecuteShellCommand(command, inv.Line, inv.Column);
                    break;
            }
        }

        private void HandleIndexAssign(IndexAssignStmt stmt)
        {
            object target = Eval(stmt.Target);
            object index = Eval(stmt.Index);
            object value = Eval(stmt.ValueExpr);

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
                    _context.CurrentScope.Variables[varName] = values[i];
                }
            }
        }

        private void HandleIf(IfStmt ifs)
        {
            object condVal = Eval(ifs.Condition);
            bool condition = ConvertToBool(condVal);

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
            object iterable = Eval(fs.Iterable);

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
                    _context.CurrentScope.Variables[fs.IteratorVar] = item;

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

                if (expr is LambdaExpr lambda)
                {
                    // capture current variables
                    var closure = _context.GetAllVariables();

                    var lambdaDecl = new FunctionStmt("<lambda>", lambda.Parameters, lambda.Body)
                    {
                        Line = expr.Line,
                        Column = expr.Column,
                    };

                    return new Function(lambdaDecl, closure);
                }

                switch (expr)
                {
                    case LiteralExpr lit:
                        return lit.Value;

                    case InterpolatedStringExpr interp:
                    {
                        var sb = new StringBuilder();
                        foreach (var part in interp.Parts)
                        {
                            object? value = Eval(part);
                            sb.Append(value?.ToString() ?? "");
                        }
                        return sb.ToString();
                    }

                    case VarExpr v:
                        return _context.LookupVariable(v.Name);

                    case BinaryExpr bin:
                        return EvalBinary(bin);

                    case CallExpr call:
                        object? callee = Eval(call.Callee);
                        if (callee is Callable callable)
                        {
                            List<object?> args = new();
                            foreach (var arg in call.Arguments)
                                args.Add(Eval(arg));
                            return callable.Call(this, args);
                        }
                        throw new RuntimeException(
                            $"Attempted to call a non-function '{callee}'",
                            expr.Line,
                            expr.Column
                        );

                    case MemberAccessExpr memberExpr:
                        object parent = Eval(memberExpr.Object);
                        return BindMember(parent, memberExpr.MemberName);

                    case ArrayExpr arr:
                        var list = new List<object?>();
                        foreach (var element in arr.Elements)
                            list.Add(Eval(element));
                        return list;

                    case IfExpr ifExpr:
                    {
                        var condVal = Eval(ifExpr.Condition);
                        bool condition = ConvertToBool(condVal);
                        if (condition)
                            return EvaluateBlockExpression(ifExpr.ThenBlock);
                        else if (ifExpr.ElseBlock != null)
                            return EvaluateBlockExpression(ifExpr.ElseBlock);
                        else
                            return null;
                    }

                    case ForExpr forExpr:
                    {
                        var iterable = Eval(forExpr.Iterable);
                        if (!(iterable is IEnumerable en))
                            throw new RuntimeException(
                                "For expression must iterate over an enumerable",
                                forExpr.Line,
                                forExpr.Column
                            );

                        var results = new List<object?>();
                        foreach (object item in en)
                        {
                            _context.PushScope();
                            _context.CurrentScope.Variables[forExpr.IteratorVar] = item;

                            try
                            {
                                var value = EvaluateBlockExpression(forExpr.Body);
                                results.Add(value);
                            }
                            finally
                            {
                                _context.PopScope();
                            }
                        }
                        return results;
                    }

                    case ParallelForExpr pf:
                        return EvalParallelFor(pf);

                    case DictExpr dict:
                        var map = new Dictionary<string, object?>();
                        foreach (var (keyExpr, valueExpr) in dict.Pairs)
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

                    case IndexExpr indexExpr:
                        object target = Eval(indexExpr.Target);
                        object indexValue = Eval(indexExpr.Index);

                        if (target is IList<object> tlist)
                        {
                            int i = ConvertToInt(indexValue);
                            if (i < 0 || i >= tlist.Count)
                                throw new RuntimeException(
                                    $"Index {i} is out of range for list of length {tlist.Count}",
                                    indexExpr.Line,
                                    indexExpr.Column
                                );
                            return tlist[i];
                        }
                        else if (target is IDictionary<string, object> tdict)
                        {
                            string key = indexValue?.ToString() ?? "";
                            if (!tdict.ContainsKey(key))
                                throw new RuntimeException(
                                    $"Key '{key}' not found in dictionary",
                                    indexExpr.Line,
                                    indexExpr.Column
                                );
                            return tdict[key];
                        }
                        else
                        {
                            throw new RuntimeException(
                                $"Type {target?.GetType().Name ?? "null"} does not support indexing",
                                indexExpr.Line,
                                indexExpr.Column
                            );
                        }

                    case RangeExpr rangeExpr:
                    {
                        object leftVal = Eval(rangeExpr.Start);
                        object rightVal = Eval(rangeExpr.End);
                        int start = ConvertToInt(leftVal);
                        int end = ConvertToInt(rightVal);
                        return new Range(start, end);
                    }

                    case SshExpr sshExpr:
                        return ExecuteSshCommand(sshExpr);

                    case ShellExpr shellCall:
                        object cmdObj = Eval(shellCall.Argument);
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

                        return ExecuteShellCommand(command, expr.Line, expr.Column);
                }

                return null;
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
                            // Create an isolated executor for this parallel task
                            var isolatedExecutor = new Executor(_scriptPath, _sourceCode);

                            // Copy relevant state
                            foreach (var variable in _context.GetAllVariables())
                                isolatedExecutor._context.GlobalScope.Variables[variable.Key] =
                                    variable.Value;

                            // Add iterator variable
                            isolatedExecutor._context.GlobalScope.Variables[pf.IteratorVar] = item;

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

            return hasOverride ? firstCancelResult : results.ToList();
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

        /// <summary>
        /// Bind a member access (obj.Member) by looking for:
        /// 1) instance methods
        /// 2) public properties
        /// 3) extension methods (including generic LINQ methods)
        /// </summary>
        private object BindMember(object parent, string memberName)
        {
            if (parent == null)
                throw new RuntimeException(
                    $"Cannot access member '{memberName}' of null",
                    _context.CurrentLocation.Line,
                    _context.CurrentLocation.Column
                );

            var type = parent.GetType();

            // 1) Instance methods (ReflectionCallable)
            var instanceMethods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (instanceMethods.Any())
            {
                return new ReflectionCallable(parent, instanceMethods);
            }

            // 2) Properties (e.g. List<T>.Count, string.Length, etc.)
            var property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
            );
            if (property != null)
            {
                return property.GetValue(parent);
            }

            // 3) Extension methods (includes LINQ: Select, Where, ToList, etc.)
            var extMethods = FindExtensionMethods(type, memberName);
            if (extMethods.Any())
            {
                return new ExtensionMethodCallable(parent, extMethods);
            }

            // 4) Nothing matched
            throw new RuntimeException(
                $"Member '{memberName}' not found on object of type {type.Name}",
                _context.CurrentLocation.Line,
                _context.CurrentLocation.Column
            );
        }

        private object EvalBinary(BinaryExpr bin)
        {
            object leftVal = Eval(bin.Left);
            object rightVal = Eval(bin.Right);

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

        private List<object?> ExecuteShellCommand(string command, int line, int column)
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
                using var proc = Process.Start(psi);
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
                return new List<object?> { stdout, stderr, exitCode, duration, pid };
            }
            catch (Exception e)
            {
                string context = _context.GetContextFragment(line);
                throw new ShellCommandException(e.Message, command, null, line, column, context, e);
            }
        }

        private List<object?> ExecuteSshCommand(SshExpr sshExpr)
        {
            object hostObj = Eval(sshExpr.Host);
            object sshCmdObj = Eval(sshExpr.Command);
            string host = hostObj?.ToString() ?? "";
            string sshCommand = EscapeForBashDoubleQuotes(sshCmdObj?.ToString() ?? "");

            try
            {
                var sw = Stopwatch.StartNew();
                string output = Helper.ExecuteSsh(host, sshCommand);
                sw.Stop();

                // Parse output and errors
                string stderr = "";
                int exitCode = 0;

                // Check for error message
                int errorIndex = output.IndexOf("\n[SSH ERROR] ");
                if (errorIndex >= 0)
                {
                    stderr = output.Substring(errorIndex + 13);
                    output = output.Substring(0, errorIndex);
                    exitCode = 1;
                }

                long latency = sw.ElapsedMilliseconds;
                string hostStatus = stderr.Length > 0 ? "error" : "connected";

                return new List<object?> { output, stderr, exitCode, hostStatus, latency };
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
        private static readonly Dictionary<(Type, string), List<MethodInfo>> _extMethodCache =
            new Dictionary<(Type, string), List<MethodInfo>>();

        private List<MethodInfo> FindExtensionMethods(Type targetType, string methodName)
        {
            var key = (targetType, methodName);
            if (_extMethodCache.TryGetValue(key, out var methods))
                return methods;

            methods = new List<MethodInfo>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            foreach (var type in asm.GetTypes())
            {
                if (!type.IsAbstract || !type.IsSealed)
                    continue; // only static classes

                foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    if (!m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var pars = m.GetParameters();
                    if (pars.Length == 0)
                        continue;

                    var first = pars[0].ParameterType;

                    // 1) exact assignable (non‐generic):
                    if (!first.ContainsGenericParameters && first.IsAssignableFrom(targetType))
                    {
                        methods.Add(m);
                        continue;
                    }

                    // 2) open‐generic: e.g. first = IEnumerable<T>
                    if (first.IsGenericType && first.ContainsGenericParameters)
                    {
                        var genericDef = first.GetGenericTypeDefinition();
                        // does targetType implement that interface?
                        if (
                            targetType.IsGenericType
                                && targetType.GetGenericTypeDefinition() == genericDef
                            || targetType
                                .GetInterfaces()
                                .Any(i =>
                                    i.IsGenericType && i.GetGenericTypeDefinition() == genericDef
                                )
                        )
                        {
                            methods.Add(m);
                        }
                    }
                }
            }
            _extMethodCache[key] = methods;
            return methods;
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
        public Dictionary<string, object> Closure { get; } // capture environment if needed

        public Function(FunctionStmt declaration, Dictionary<string, object> closure)
        {
            Declaration = declaration;
            Closure = closure;
        }

        public object? Call(Executor executor, List<object?> arguments)
        {
            // Create new environment (shallow copy of closure)
            var localEnv = new Dictionary<string, object>(Closure);
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
            // 1) Pick overloads by arity
            var name = _methods.First().Name;
            var cands = _methods
                .Where(m => m.GetParameters().Length == arguments.Count + 1)
                .ToList();
            if (!cands.Any())
                throw new RuntimeException(
                    $"No extension method '{name}' for type {_target.GetType().Name} accepts {arguments.Count} args",
                    executor.Context.CurrentLocation.Line,
                    executor.Context.CurrentLocation.Column
                );

            MethodInfo method = cands.First();

            // 2) Close generics if needed (e.g. Enumerable.Select<TSource, TResult>)
            if (method.IsGenericMethodDefinition)
            {
                // figure out TSource from the target (List<T> or IQueryable<T> etc.)
                Type targetType = _target.GetType();
                // assume first parameter is IEnumerable<TSource>
                Type enumIface = method.GetParameters()[0].ParameterType.GetGenericTypeDefinition();
                Type elementType;
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == enumIface)
                    elementType = targetType.GetGenericArguments()[0];
                else
                    elementType = targetType
                        .GetInterfaces()
                        .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == enumIface)
                        .GetGenericArguments()[0];

                // for Shelltrac we’ll project everything to object
                Type resultType = typeof(object);

                method = method.MakeGenericMethod(elementType, resultType);
            }

            // 3) Build invocation args (first slot = the instance)
            var pars = method.GetParameters();
            object?[] invokeArgs = new object?[pars.Length];
            invokeArgs[0] = _target;

            for (int i = 0; i < arguments.Count; i++)
            {
                Type pType = pars[i + 1].ParameterType;
                var arg = arguments[i];

                if (typeof(Delegate).IsAssignableFrom(pType) && arg is Callable shellFn)
                {
                    // wrap a Shelltrac Callable into the required delegate
                    invokeArgs[i + 1] = CreateDelegateForShellFn(shellFn, executor, pType);
                }
                else
                {
                    // simple conversion (int, string, etc.)
                    invokeArgs[i + 1] = Convert.ChangeType(arg, pType);
                }
            }

            // 4) Invoke the static extension method
            return method.Invoke(null, invokeArgs);
        }

        // same helper as before, no changes needed
        private static Delegate CreateDelegateForShellFn(
            Callable shellFn,
            Executor executor,
            Type delegateType
        )
        {
            var invokeInfo = delegateType.GetMethod("Invoke")!;
            var paras = invokeInfo.GetParameters();

            // build lambda parameters
            var paramExprs = paras
                .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                .ToArray();

            // pack into List<object>
            var listType = typeof(List<object>);
            var ctor = listType.GetConstructor(Type.EmptyTypes)!;
            var addMethod = listType.GetMethod("Add")!;
            var listInit = Expression.ListInit(
                Expression.New(ctor),
                paramExprs.Select(pe =>
                    Expression.ElementInit(addMethod, Expression.Convert(pe, typeof(object)))
                )
            );

            // call shellFn.Call(executor, list)
            var callMethod = typeof(Callable).GetMethod("Call")!;
            var shellFnConst = Expression.Constant(shellFn);
            var execConst = Expression.Constant(executor, typeof(Executor));
            var callExpr = Expression.Call(shellFnConst, callMethod, execConst, listInit);

            // unwrap return
            Expression body =
                invokeInfo.ReturnType == typeof(void)
                    ? Expression.Block(callExpr, Expression.Default(typeof(void)))
                    : Expression.Convert(callExpr, invokeInfo.ReturnType);

            // compile into the desired delegate
            var lambda = Expression.Lambda(delegateType, body, paramExprs);
            return lambda.Compile();
        }
    }
}
