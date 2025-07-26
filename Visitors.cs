namespace Shelltrac
{
    public interface IStmtVisitor
    {
        void Visit(ExpressionStmt stmt);
        void Visit(TaskStmt stmt);
        void Visit(EventStmt stmt);
        void Visit(FunctionStmt stmt);
        void Visit(ReturnStmt stmt);
        void Visit(TriggerStmt stmt);
        void Visit(DestructuringAssignStmt stmt);
        void Visit(InvocationStmt stmt);
        void Visit(VarDeclStmt stmt);
        void Visit(AssignStmt stmt);
        void Visit(IndexAssignStmt stmt);
        void Visit(IfStmt stmt);
        void Visit(ForStmt stmt);
        void Visit(LoopYieldStmt stmt);
    }

    public interface IExprVisitor
    {
        EvalResult Visit(LiteralExpr expr);
        EvalResult Visit(InterpolatedStringExpr expr);
        EvalResult Visit(VarExpr expr);
        EvalResult Visit(CallExpr expr);
        EvalResult Visit(BinaryExpr expr);
        EvalResult Visit(IfExpr expr);
        EvalResult Visit(ForExpr expr);
        EvalResult Visit(ParallelForExpr expr);
        EvalResult Visit(LambdaExpr expr);
        EvalResult Visit(ShellExpr expr);
        EvalResult Visit(SshExpr expr);
        EvalResult Visit(MemberAccessExpr expr);
        EvalResult Visit(RangeExpr expr);
        EvalResult Visit(ArrayExpr expr);
        EvalResult Visit(IndexExpr expr);
        EvalResult Visit(DictExpr expr);
    }
}