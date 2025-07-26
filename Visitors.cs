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

    public interface IExprVisitor<T>
    {
        T Visit(LiteralExpr expr);
        T Visit(InterpolatedStringExpr expr);
        T Visit(VarExpr expr);
        T Visit(CallExpr expr);
        T Visit(BinaryExpr expr);
        T Visit(IfExpr expr);
        T Visit(ForExpr expr);
        T Visit(ParallelForExpr expr);
        T Visit(LambdaExpr expr);
        T Visit(ShellExpr expr);
        T Visit(SshExpr expr);
        T Visit(MemberAccessExpr expr);
        T Visit(RangeExpr expr);
        T Visit(ArrayExpr expr);
        T Visit(IndexExpr expr);
        T Visit(DictExpr expr);
    }
}