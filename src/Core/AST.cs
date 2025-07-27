using System.Collections.Generic;

namespace Shelltrac
{
    public class SourceLocation
    {
        public int Line { get; }
        public int Column { get; }
        public string Context { get; }
        public string ScriptName { get; }

        public SourceLocation(int line, int column, string context = "", string scriptName = "")
        {
            Line = line;
            Column = column;
            Context = context;
            ScriptName = scriptName;
        }

        public SourceLocation(Stmt statement, string scriptName = "")
        {
            Line = statement.Line;
            Column = statement.Column;
            Context = $"{statement.GetType().Name} at line {Line}";
            ScriptName = scriptName;
        }

        public SourceLocation(Expr expression, string scriptName = "")
        {
            Line = expression.Line;
            Column = expression.Column;
            Context = $"{expression.GetType().Name} at line {Line}";
            ScriptName = scriptName;
        }

        public override string ToString()
        {
            string location = !string.IsNullOrEmpty(ScriptName) ? $"{ScriptName}:" : "";
            return $"{location}{Line}:{Column} ({Context})";
        }
    }

    public abstract class Stmt
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public int Length { get; set; }
        
        public abstract void Accept(IStmtVisitor visitor);
    }

    public class ProgramNode
    {
        public List<Stmt> Statements { get; } = new();
    }

    // ----- Existing DSL constructs -----

    public class ExpressionStmt : Stmt
    {
        public Expr Expression { get; }

        public ExpressionStmt(Expr expression)
        {
            Expression = expression;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class TaskStmt : Stmt
    {
        public string Name { get; }
        public bool IsOnce { get; }
        public int Frequency { get; }
        public List<Stmt> Body { get; }

        public TaskStmt(string name, bool isOnce, int freq, List<Stmt> body)
        {
            Name = name;
            IsOnce = isOnce;
            Body = body;
            Frequency = freq;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class EventStmt : Stmt
    {
        public string EventName { get; }
        public List<String> Parameters { get; }
        public List<Stmt> Body { get; }

        public EventStmt(string eventName, List<String> parameters, List<Stmt> body)
        {
            EventName = eventName;
            Parameters = parameters;
            Body = body;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class FunctionStmt : Stmt
    {
        public string Name { get; }
        public List<string> Parameters { get; }
        public List<Stmt> Body { get; }
        public List<FunctionAttribute> Attributes { get; }

        public FunctionStmt(string name, List<string> parameters, List<Stmt> body, List<FunctionAttribute>? attributes = null)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            Attributes = attributes ?? new List<FunctionAttribute>();
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    // represents an inline fn(a,b){ … } expression
    public class LambdaExpr : Expr
    {
        public List<string> Parameters { get; }
        public List<Stmt> Body { get; }

        public LambdaExpr(List<string> parameters, List<Stmt> body)
        {
            Parameters = parameters;
            Body = body;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class ReturnStmt : Stmt
    {
        public List<Expr> Values { get; }

        public ReturnStmt(List<Expr> values) => Values = values;
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class TriggerStmt : Stmt
    {
        public string EventName { get; }
        public List<Expr> Arguments { get; }

        public TriggerStmt(string eventName, List<Expr> arguments)
        {
            EventName = eventName;
            Arguments = arguments;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class DestructuringAssignStmt : Stmt
    {
        public List<string> VarNames { get; }
        public Expr ValueExpr { get; }

        public DestructuringAssignStmt(List<string> varNames, Expr valueExpr)
        {
            VarNames = varNames;
            ValueExpr = valueExpr;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class InvocationStmt : Stmt
    {
        public string CommandKeyword; // "log" or "sh"
        public Expr Argument; // Now it's an Expr (instead of a raw string)

        public InvocationStmt(string keyword, Expr arg)
        {
            CommandKeyword = keyword;
            Argument = arg;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    // ----- New: Variables, if, for, etc. -----

    public class VarDeclStmt : Stmt
    {
        public string VarName { get; }
        public Expr Initializer { get; }

        public VarDeclStmt(string varName, Expr init)
        {
            VarName = varName;
            Initializer = init;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class AssignStmt : Stmt
    {
        public string VarName { get; }
        public Expr ValueExpr { get; }

        public AssignStmt(string varName, Expr valueExpr)
        {
            VarName = varName;
            ValueExpr = valueExpr;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class IndexAssignStmt : Stmt
    {
        public Expr Target { get; }
        public Expr Index { get; }
        public Expr ValueExpr { get; }

        public IndexAssignStmt(Expr target, Expr index, Expr valueExpr)
        {
            Target = target;
            Index = index;
            ValueExpr = valueExpr;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class IfStmt : Stmt
    {
        public Expr Condition { get; }
        public List<Stmt> ThenBlock { get; }
        public List<Stmt>? ElseBlock { get; }

        public IfStmt(Expr condition, List<Stmt> thenBlock, List<Stmt>? elseBlock)
        {
            Condition = condition;
            ThenBlock = thenBlock;
            ElseBlock = elseBlock;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class ForStmt : Stmt
    {
        public string IteratorVar { get; }
        public Expr Iterable { get; }
        public List<Stmt> Body { get; }

        public ForStmt(string iteratorVar, Expr iterable, List<Stmt> body)
        {
            IteratorVar = iteratorVar;
            Iterable = iterable;
            Body = body;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class WhileStmt : Stmt
    {
        public Expr Condition { get; }
        public List<Stmt> Body { get; }

        public WhileStmt(Expr condition, List<Stmt> body)
        {
            Condition = condition;
            Body = body;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    // ----- Expressions -----

    public abstract class Expr
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public int Length { get; set; }
        
        public abstract T Accept<T>(IExprVisitor<T> visitor);
    }

    public class LiteralExpr : Expr
    {
        public object Value { get; }

        public LiteralExpr(object value) => Value = value;
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class InterpolatedStringExpr : Expr
    {
        public List<Expr> Parts { get; }

        public InterpolatedStringExpr(List<Expr> parts)
        {
            Parts = parts;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class VarExpr : Expr
    {
        public string Name { get; }

        public VarExpr(string name) => Name = name;
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class CallExpr : Expr
    {
        public Expr Callee { get; }
        public List<Expr> Arguments { get; }

        public CallExpr(Expr callee, List<Expr> arguments)
        {
            Callee = callee;
            Arguments = arguments;
        }
        
        public bool IsUfcsCandidate => Callee is MemberAccessExpr;
        
        public (Expr Object, string MethodName)? GetUfcsInfo()
        {
            if (Callee is MemberAccessExpr memberAccess)
            {
                return (memberAccess.Object, memberAccess.MemberName);
            }
            return null;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class BinaryExpr : Expr
    {
        public Expr Left { get; }
        public string Op { get; } // e.g. "<", "+"
        public Expr Right { get; }

        public BinaryExpr(Expr left, string op, Expr right)
        {
            Left = left;
            Op = op;
            Right = right;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class IfExpr : Expr
    {
        public Expr Condition { get; }
        public List<Stmt> ThenBlock { get; }
        public List<Stmt>? ElseBlock { get; }

        public IfExpr(Expr condition, List<Stmt> thenBlock, List<Stmt>? elseBlock)
        {
            Condition = condition;
            ThenBlock = thenBlock;
            ElseBlock = elseBlock;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class ForExpr : Expr
    {
        public string IteratorVar { get; }
        public Expr Iterable { get; }
        public List<Stmt> Body { get; }

        public ForExpr(string iteratorVar, Expr iterable, List<Stmt> body)
        {
            IteratorVar = iteratorVar;
            Iterable = iterable;
            Body = body;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class LoopYieldStmt : Stmt
    {
        public Expr Value { get; }

        // When IsEmit is true, the value is appended to the collection.
        // When false, the value is a return value that terminates the iteration.
        public bool IsEmit { get; }

        // If true, this “return” cancels all iterations.
        public bool IsGlobalCancel { get; }

        // If true, then the overall result is overridden by this value.
        public bool IsOverride { get; }

        public LoopYieldStmt(Expr value, bool isEmit, bool isGlobalCancel, bool isOverride)
        {
            Value = value;
            IsEmit = isEmit;
            IsGlobalCancel = isGlobalCancel;
            IsOverride = isOverride;
        }
        
        public override void Accept(IStmtVisitor visitor) => visitor.Visit(this);
    }

    public class ParallelForExpr : Expr
    {
        public string IteratorVar { get; }
        public Expr Iterable { get; }
        public List<Stmt> Body { get; }

        public ParallelForExpr(string iteratorVar, Expr iterable, List<Stmt> body)
        {
            IteratorVar = iteratorVar;
            Iterable = iterable;
            Body = body;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class ShellExpr : Expr
    {
        public Expr Argument { get; }
        public ParserConfig Parser { get; }

        public ShellExpr(Expr arg, ParserConfig parser = null)
        {
            Argument = arg;
            Parser = parser;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public abstract class ParserConfig { }

    public class FunctionParserConfig : ParserConfig
    {
        public LambdaExpr LineProcessor { get; }

        public FunctionParserConfig(LambdaExpr lineProcessor)
        {
            LineProcessor = lineProcessor;
        }
    }

    public class ObjectParserConfig : ParserConfig
    {
        public LambdaExpr? Setup { get; }
        public LambdaExpr LineProcessor { get; }
        public LambdaExpr? Complete { get; }
        public LambdaExpr? ErrorHandler { get; }

        public ObjectParserConfig(
            LambdaExpr? setup,
            LambdaExpr lineProcessor,
            LambdaExpr? complete = null,
            LambdaExpr? errorHandler = null)
        {
            Setup = setup;
            LineProcessor = lineProcessor;
            Complete = complete;
            ErrorHandler = errorHandler;
        }
    }

    // Format-specific shortcuts: parse as json|csv
    public class FormatParserConfig : ParserConfig
    {
        public string Format { get; }

        public FormatParserConfig(string format)
        {
            Format = format;
        }
    }

    public class SshExpr : Expr
    {
        public Expr Host { get; }
        public Expr Command { get; }
        public ParserConfig Parser { get; }

        public SshExpr(Expr host, Expr command, ParserConfig parser = null)
        {
            Host = host;
            Command = command;
            Parser = parser;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class MemberAccessExpr : Expr
    {
        public Expr Object { get; }
        public string MemberName { get; }

        public MemberAccessExpr(Expr obj, string memberName)
        {
            Object = obj;
            MemberName = memberName;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class RangeExpr : Expr
    {
        public Expr Start { get; }
        public Expr End { get; }

        public RangeExpr(Expr start, Expr end)
        {
            Start = start;
            End = end;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class ArrayExpr : Expr
    {
        public List<Expr> Elements { get; }

        public ArrayExpr(List<Expr> elements)
        {
            Elements = elements;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class IndexExpr : Expr
    {
        public Expr Target { get; }
        public Expr Index { get; }

        public IndexExpr(Expr target, Expr index)
        {
            Target = target;
            Index = index;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class DictExpr : Expr
    {
        public List<(Expr Key, Expr Value)> Pairs { get; }

        public DictExpr(List<(Expr Key, Expr Value)> pairs)
        {
            Pairs = pairs;
        }

        public Expr? ExtractStringKey(string key)
        {
            foreach (var kv in Pairs)
            {
                if (kv.Key is LiteralExpr litExpr && litExpr.Value is string strValue && strValue == key)
                {
                    return kv.Value;
                }
            }
            return null;
        }
        
        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    public class TimeLiteralExpr : Expr
    {
        public int Value { get; }
        public string Unit { get; }
        public long TotalMilliseconds { get; }

        public TimeLiteralExpr(int value, string unit)
        {
            Value = value;
            Unit = unit.ToLower();
            TotalMilliseconds = CalculateMilliseconds(value, Unit);
        }

        private static long CalculateMilliseconds(int value, string unit)
        {
            return unit switch
            {
                "ms" => value,
                "s" => value * 1000L,
                "m" => value * 60L * 1000L,
                "h" => value * 60L * 60L * 1000L,
                "d" => value * 24L * 60L * 60L * 1000L,
                _ => throw new System.ArgumentException($"Invalid time unit: {unit}")
            };
        }

        public override T Accept<T>(IExprVisitor<T> visitor) => visitor.Visit(this);
    }

    // Attribute system for functions
    public class FunctionAttribute
    {
        public string Name { get; }
        public Dictionary<string, object?> Parameters { get; }

        public FunctionAttribute(string name, Dictionary<string, object?> parameters)
        {
            Name = name;
            Parameters = parameters;
        }
    }
}
