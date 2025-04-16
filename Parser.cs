using System;
using System.Collections.Generic;
using System.Text;

namespace Shelltrac
{
    public class DSLParser
    {
        private readonly List<Token> _tokens;
        private readonly string _source;
        private int _current;
        private bool inParallelLoop = false;

        public DSLParser(List<Token> tokens, string source)
        {
            _tokens = tokens;
            _source = source;
        }

        public ProgramNode ParseProgram()
        {
            var program = new ProgramNode();
            while (!IsAtEnd())
            {
                var stmt = ParseStatement();
                if (stmt != null)
                    program.Statements.Add(stmt);
            }
            return program;
        }

        private Stmt ParseStatement()
        {
            if (Match(TokenType.TASK))
                return ParseTask();
            if (Match(TokenType.ON))
                return ParseEvent();
            if (Match(TokenType.TRIGGER))
                return ParseTrigger();
            if (Match(TokenType.LOG, TokenType.SH, TokenType.SSH))
                return ParseInvocation();
            if (Match(TokenType.LET))
                return ParseVarDecl();
            if (Match(TokenType.IF))
                return ParseIf();
            if (Match(TokenType.FOR))
                return ParseFor();
            if (Match(TokenType.EMIT))
            {
                var value = ParseExpression();
                return new LoopYieldStmt(
                    value,
                    isEmit: true,
                    isGlobalCancel: false,
                    isOverride: false
                );
            }

            if (Match(TokenType.FN))
                return ParseFunction();
            if (Match(TokenType.RETURN))
                return ParseReturnOrLoopYield(this.inParallelLoop);
            // Attempt to parse an assignment statement:
            int savedCurrent = _current;
            Expr lhs = ParseAssignable(); // this parses a variable and any index accesses
            if (Match(TokenType.EQUAL))
            { // if '=' is next, it’s an assignment!
                Expr rhs = ParseExpression();
                if (lhs is VarExpr varExpr)
                    return new AssignStmt(varExpr.Name, rhs);
                else if (lhs is IndexExpr indexExpr)
                    return new IndexAssignStmt(indexExpr.Target, indexExpr.Index, rhs);
                else
                    throw new Exception("Invalid assignment target");
            }
            else
            {
                // Not an assignment—reset and parse a normal expression statement.
                _current = savedCurrent;
                Expr expr = ParseExpression();
                Match(TokenType.SEMICOLON); // optional semicolon
                return new ExpressionStmt(expr);
            } // unknown or end
            Token token = Peek();
            throw new ParsingException(
                $"Unexpected token '{token.Lexeme}' encountered.",
                Peek().Line,
                Peek().Column
            );
        }

        private Stmt ParseFunction()
        {
            var nameToken = Consume(TokenType.IDENTIFIER, "Expected function name after 'fn'");
            string functionName = nameToken.Lexeme;

            Consume(TokenType.LPAREN, "Expected '(' after function name");
            List<string> parameters = new();
            if (!Check(TokenType.RPAREN))
            {
                do
                {
                    var paramToken = Consume(TokenType.IDENTIFIER, "Expected parameter name");
                    parameters.Add(paramToken.Lexeme);
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RPAREN, "Expected ')' after parameter list"); // Adjust token types if you introduce LPAREN/RPAREN.

            var body = ParseBlock();
            return new FunctionStmt(functionName, parameters, body);
        }

        private Stmt ParseReturn()
        {
            List<Expr> values = new List<Expr>();

            if (!Check(TokenType.SEMICOLON))
            {
                // Parse first expression
                values.Add(ParseExpression());

                // Check for additional expressions separated by commas
                while (Match(TokenType.COMMA))
                {
                    values.Add(ParseExpression());
                }
            }

            Match(TokenType.SEMICOLON); // Optional semicolon
            return new ReturnStmt(values);
        }

        private Stmt ParseReturnOrLoopYield(bool inParallelLoop)
        {
            if (inParallelLoop)
            {
                // First parse the expression to return
                var value = ParseExpression();
                bool globalCancel = false;
                bool overrideResult = false;

                // Check for 'cancel' token - THE PROBLEM IS HERE
                // It's trying to match IDENTIFIER but 'cancel' isn't recognized this way

                // Current code that doesn't work:
                // if (Match(TokenType.IDENTIFIER) && Previous().Lexeme.Equals("cancel", StringComparison.OrdinalIgnoreCase)) {

                // Modified code:
                if (Match(TokenType.CANCEL))
                {
                    Console.WriteLine("FOUND CANCEL");
                    Advance(); // Consume the 'cancel' token
                    globalCancel = true;

                    if (Match(TokenType.OVERRIDE))
                    {
                        Console.WriteLine("FOUND OVERRIDE");
                        Advance(); // Consume the 'override' token
                        overrideResult = true;
                    }
                }

                // Consume any semicolon if present
                Match(TokenType.SEMICOLON);
                return new LoopYieldStmt(
                    value,
                    isEmit: false,
                    isGlobalCancel: globalCancel,
                    isOverride: overrideResult
                );
            }
            else
            {
                return ParseReturn();
            }
        }

        private Stmt ParseTask()
        {
            // TODO probably broke this with the verbatim/interpolated string changes
            var nameToken = Consume(TokenType.STRING, "Expected string after 'task'");
            string taskName = nameToken.Lexeme;

            bool isOnce = false;
            int frequency = -1;
            if (Match(TokenType.ONCE))
                isOnce = true;
            else if (Match(TokenType.EVERY))
            {
                // ignoring the schedule details for MVP, skip next token if numeric
                if (Check(TokenType.NUMBER))
                {
                    var freq = Consume(TokenType.NUMBER, "Expected task frequency in ms").Lexeme;
                    frequency = int.Parse(freq); // TODO error handling
                }
            }

            Consume(TokenType.ARROW, "Expected '=>' after task declaration");
            Consume(TokenType.DO, "Expected 'do' keyword");
            var body = ParseBlock();
            return new TaskStmt(taskName, isOnce, frequency, body);
        }

        private Expr ParseArray()
        {
            // Consume '[' token
            var elements = new List<Expr>();
            if (!Check(TokenType.RBRACKET))
            {
                do
                {
                    elements.Add(ParseExpression());
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RBRACKET, "Expected ']' after array elements");
            return new ArrayExpr(elements);
        }

        private Expr ParseDictionary()
        {
            // Consume '{' token (if not a shell block)
            var pairs = new List<(Expr, Expr)>();
            if (!Check(TokenType.RBRACE))
            {
                do
                {
                    // Expect key (likely a string or identifier)
                    Expr key = ParseExpression();
                    Consume(TokenType.COLON, "Expected ':' between key and value in dictionary");
                    Expr value = ParseExpression();
                    pairs.Add((key, value));
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RBRACE, "Expected '}' after dictionary entries");
            return new DictExpr(pairs);
        }

        private Stmt ParseEvent()
        {
            var eventName = Consume(TokenType.IDENTIFIER, "Expected event name").Lexeme;
            List<string> parameters = new List<string>();
            if (Match(TokenType.LPAREN))
            {
                if (!Check(TokenType.RPAREN))
                {
                    do
                    {
                        parameters.Add(
                            Consume(TokenType.IDENTIFIER, "Expected parameter name").Lexeme
                        );
                    } while (Match(TokenType.COMMA));
                }
                Consume(TokenType.RPAREN, "Expected ')' after event parameter list");
            }
            Consume(TokenType.ARROW, "Expected '=>' after event name");
            Consume(TokenType.DO, "Expected 'do'");
            var body = ParseBlock();
            return new EventStmt(eventName, parameters, body);
        }

        private Stmt ParseTrigger()
        {
            var ev = Consume(TokenType.IDENTIFIER, "Expected event name after 'trigger'");
            List<Expr> args = new List<Expr>();
            if (Match(TokenType.LPAREN))
            {
                if (!Check(TokenType.RPAREN))
                {
                    do
                    {
                        args.Add(ParseExpression());
                    } while (Match(TokenType.COMMA));
                }
                Consume(TokenType.RPAREN, "Expected ')' after event arguments");
            }
            return new TriggerStmt(ev.Lexeme, args);
        }

        private Stmt ParseInvocation()
        {
            // we matched LOG or SH
            var keyword = Previous().Lexeme; // "log" or "sh"
            // parse an expression for the argument
            Expr expr;
            //Console.WriteLine($"Encountered invocation with keyword {keyword}");
            if (keyword == "ssh")
            {
                // parse the host
                Expr hostExpr = ParseExpression();
                // if the command is given as a block, use the special parser
                //
                Expr commandExpr;
                if (Check(TokenType.LBRACE))
                {
                    Console.WriteLine("shell keyword saw LBRACE, parsing as shell block");
                    commandExpr = ParseShellBlock();
                }
                else
                {
                    Console.WriteLine("shell keyword NO saw LBRACE, parsing as expr");
                    commandExpr = ParseExpression();
                }
                return new InvocationStmt(keyword, new SshExpr(hostExpr, commandExpr));
            }
            else if (keyword == "sh" && Check(TokenType.LBRACE))
            {
                expr = ParseShellBlock();
            }
            else
            {
                expr = ParseExpression();
            }
            return new InvocationStmt(keyword, expr);
        }

        private Expr ParseShellBlock()
        {
            // Consume the opening '{' token and record its start position.
            Token openBrace = Consume(TokenType.LBRACE, "Expected '{' to start a shell block.");
            int startPos = openBrace.StartIndex;

            // Use the raw _source to scan for the matching '}'
            int braceCount = 1;
            int pos = startPos + 1; // start right after the opening brace

            while (pos < _source.Length && braceCount > 0)
            {
                char c = _source[pos];
                if (c == '{')
                    braceCount++;
                else if (c == '}')
                    braceCount--;
                pos++;
            }

            if (braceCount != 0)
                throw new ParsingException(
                    "Unbalanced braces in shell block.",
                    Peek().Line,
                    Peek().Column
                );

            // Extract the raw block (including outer braces)
            string rawBlock = _source.Substring(startPos, pos - startPos);

            // Optionally, trim off the outer braces:
            if (rawBlock.StartsWith("{") && rawBlock.EndsWith("}"))
                rawBlock = rawBlock.Substring(1, rawBlock.Length - 2).Trim();

            // Now, update the token pointer to skip over tokens that fall inside the shell block.
            while (_current < _tokens.Count && _tokens[_current].StartIndex < pos)
            {
                _current++;
            }

            return new LiteralExpr(rawBlock);
        }

        private Stmt ParseVarDecl()
        {
            // Check ahead to see if this is a destructuring declaration
            if (
                Check(TokenType.IDENTIFIER)
                && (
                    Peek(1).Type == TokenType.COMMA
                    || (Peek(1).Type == TokenType.IDENTIFIER && Peek(2).Type == TokenType.COMMA)
                )
            )
            {
                return ParseDestructuringDecl();
            }

            // Original var declaration logic
            var nameToken = Consume(TokenType.IDENTIFIER, "Expected variable name after 'let'");
            Consume(TokenType.EQUAL, "Expected '=' after variable name");
            var initializer = ParseExpression();
            return new VarDeclStmt(nameToken.Lexeme, initializer);
        }

        private Stmt ParseDestructuringDecl()
        {
            // Consume the 'let' token if we haven't already
            // TODO: why do we do that? to prevent let x = 5; x, y = foobar();
            if (Previous().Type != TokenType.LET)
                Consume(TokenType.LET, "Expected 'let' before variable destructuring");

            List<string> varNames = new List<string>();

            // Parse first variable name
            varNames.Add(Consume(TokenType.IDENTIFIER, "Expected variable name").Lexeme);

            // Parse additional variable names
            while (Match(TokenType.COMMA))
            {
                if (Check(TokenType.IDENTIFIER))
                {
                    varNames.Add(Consume(TokenType.IDENTIFIER, "Expected variable name").Lexeme);
                }
                else if (Match(TokenType.UNDERSCORE)) // Handle _ for ignoring values
                {
                    varNames.Add("_"); // Special marker for ignored values
                }
                else
                {
                    throw new ParsingException(
                        "Expected variable name or _ in destructuring assignment",
                        Peek().Line,
                        Peek().Column
                    );
                }
            }

            Consume(TokenType.EQUAL, "Expected '=' after variable names");
            var expr = ParseExpression();

            return new DestructuringAssignStmt(varNames, expr);
        }

        private Expr ParseAssignable()
        {
            // Start by parsing a primary expression (could be a variable, literal, etc.)
            Expr expr = ParsePrimary();
            // Handle index accesses like NEEDS_CHECK[machine]
            while (Match(TokenType.LBRACKET))
            {
                Expr index = ParseExpression();
                Consume(TokenType.RBRACKET, "Expected ']' after index expression");
                expr = new IndexExpr(expr, index);
            }
            return expr;
        }

        private Stmt ParseAssignment()
        {
            // Instead of consuming a bare IDENTIFIER, we parse a full assignable expression.
            Expr lhs = ParseAssignable();
            Consume(TokenType.EQUAL, "Expected '=' in assignment");
            Expr valueExpr = ParseExpression();

            if (lhs is VarExpr varExpr)
                return new AssignStmt(varExpr.Name, valueExpr);
            else if (lhs is IndexExpr indexExpr)
                return new IndexAssignStmt(indexExpr.Target, indexExpr.Index, valueExpr);
            else
                throw new ParsingException(
                    $"Invalid assignment target {lhs}",
                    Peek().Line,
                    Peek().Column
                );
        }

        // If and For also exist as Stmts for assignment!
        private Stmt ParseIf()
        {
            // if expr { block } (else if expr { block })* (else { block })?
            var condition = ParseExpression();
            var thenBlock = ParseBlock();
            List<Stmt>? elseBlock = null;

            if (Match(TokenType.ELSE))
            {
                // Check for "else if"
                if (Match(TokenType.IF))
                {
                    // Parse the nested if statement
                    elseBlock = new List<Stmt> { ParseIf() };
                }
                else
                {
                    // Regular "else" block
                    elseBlock = ParseBlock();
                }
            }

            return new IfStmt(condition, thenBlock, elseBlock);
        }

        private Stmt ParseFor()
        {
            // for i in expr..expr { block }
            var varName = Consume(TokenType.IDENTIFIER, "Expected iterator variable").Lexeme;
            _ = Consume(TokenType.IDENTIFIER, "Expected 'in' or something similar").Lexeme; // ignoring real check
            var iterable = ParseExpression();
            var body = ParseBlock();
            return new ForStmt(varName, iterable, body);
        }

        private Expr ParseIfExpr()
        {
            // Already consumed IF
            var condition = ParseExpression();
            var thenBlock = ParseBlock(); // reuse your block parser
            List<Stmt>? elseBlock = null;
            if (Match(TokenType.ELSE))
            {
                elseBlock = ParseBlock();
            }
            return new IfExpr(condition, thenBlock, elseBlock);
        }

        private Expr ParseForExpr()
        {
            // Already consumed FOR
            var iteratorToken = Consume(
                TokenType.IDENTIFIER,
                "Expected iterator variable after 'for'"
            );
            string iteratorVar = iteratorToken.Lexeme;
            var inToken = Consume(TokenType.IDENTIFIER, "Expected 'in' after iterator variable");
            if (!inToken.Lexeme.Equals("in", StringComparison.OrdinalIgnoreCase))
                throw new ParsingException(
                    "Expected 'in' after iterator variable in for expression",
                    Peek().Line,
                    Peek().Column
                );
            var iterable = ParseExpression();
            var body = ParseBlock();
            return new ForExpr(iteratorVar, iterable, body);
        }

        private Expr ParseParallelForExpr()
        {
            var iteratorToken = Consume(
                TokenType.IDENTIFIER,
                "Expected iterator variable after 'for'"
            );
            this.inParallelLoop = true;
            string iteratorVar = iteratorToken.Lexeme;
            // Consume the 'in' keyword explicitly.
            var inToken = Consume(TokenType.IDENTIFIER, "Expected 'in' after iterator variable");
            if (!inToken.Lexeme.Equals("in", StringComparison.OrdinalIgnoreCase))
                throw new ParsingException(
                    "Expected 'in' after iterator variable in parallel for expression",
                    Peek().Line,
                    Peek().Column
                );
            var iterable = ParseExpression();
            var body = ParseBlock();

            this.inParallelLoop = false;
            return new ParallelForExpr(iteratorVar, iterable, body);
        }

        private List<Stmt> ParseBlock()
        {
            Consume(TokenType.LBRACE, "Expected '{'");
            var statements = new List<Stmt>();
            while (!Check(TokenType.RBRACE) && !IsAtEnd())
            {
                var stmt = ParseStatement();
                if (stmt != null)
                    statements.Add(stmt);
            }
            Consume(TokenType.RBRACE, "Expected '}' after block");
            return statements;
        }

        // ---------- Expression parsing for < and + ----------

        private Expr ParseExpression() => ParseRange();

        // TODO does this make sense? 3>5..7?
        private Expr ParseRange()
        {
            Expr expr = ParseComparison();
            if (Match(TokenType.DOTDOT))
            {
                Expr right = ParseComparison();
                expr = new RangeExpr(expr, right);
            }

            return expr;
        }

        private Expr ParseComparison()
        {
            var expr = ParseTerm();

            while (
                Match(
                    TokenType.LESS,
                    TokenType.LESS_EQUAL,
                    TokenType.GREATER,
                    TokenType.GREATER_EQUAL,
                    TokenType.EQUAL_EQUAL,
                    TokenType.NOT_EQUAL
                )
            )
            {
                var op = Previous().Lexeme; // Get the operator
                var right = ParseTerm();
                expr = new BinaryExpr(expr, op, right);
            }

            return ParsePostfix(expr);
        }

        private Expr ParseTerm()
        {
            var expr = ParseFactor();
            while (Match(TokenType.PLUS, TokenType.MINUS))
            {
                var op = Previous().Lexeme; // "+" or "-"
                var right = ParseFactor();
                expr = new BinaryExpr(expr, op, right);
            }
            return expr;
        }

        private Expr ParseFactor()
        {
            var expr = ParseUnary();
            while (Match(TokenType.STAR, TokenType.SLASH))
            {
                var op = Previous().Lexeme; // "*" or "/"
                var right = ParseUnary();
                expr = new BinaryExpr(expr, op, right);
            }
            return expr;
        }

        private Expr ParseUnary()
        {
            // For now just handles primary, but could support unary operators later
            return ParsePostfix(ParsePrimary());
        }

        private Expr ParsePrimary()
        {
            if (Match(TokenType.STRING))
            {
                return new LiteralExpr(Previous().Lexeme);
            }

            if (Match(TokenType.VERBATIM_STRING))
            {
                return new LiteralExpr(Previous().Lexeme);
            }

            if (Match(TokenType.INTERPOLATED_STRING))
            {
                return ParseInterpolatedString();
            }
            if (Match(TokenType.NUMBER))
            {
                return new LiteralExpr(Int32.Parse(Previous().Lexeme));
            }
            if (Match(TokenType.TRUE))
            {
                return new LiteralExpr(true);
            }
            if (Match(TokenType.FALSE))
            {
                return new LiteralExpr(false);
            }

            if (Match(TokenType.IF))
                return ParseIfExpr();
            if (Match(TokenType.FOR))
                return ParseForExpr();

            if (Match(TokenType.PARALLEL))
            {
                Consume(TokenType.FOR, "Expected 'for' after 'parallel'");
                return ParseParallelForExpr();
            }
            if (Match(TokenType.LPAREN))
            {
                Expr expr = ParseExpression();
                Consume(TokenType.RPAREN, "Expected ')' after expression.");
                return expr;
            }

            if (Match(TokenType.LBRACKET))
            {
                return ParseArray();
            }
            if (Match(TokenType.IDENTIFIER))
            {
                Expr expr = new VarExpr(Previous().Lexeme);
                // Check for a function call (i.e. '(' following identifier).
                if (Match(TokenType.LPAREN)) // ideally use LPAREN instead of LBRACKET if available
                {
                    var arguments = new List<Expr>();
                    if (!Check(TokenType.RPAREN))
                    {
                        do
                        {
                            arguments.Add(ParseExpression());
                        } while (Match(TokenType.COMMA));
                    }
                    Consume(TokenType.RPAREN, "Expected ')' after arguments"); // change token if needed
                    expr = new CallExpr(expr, arguments);
                }
                return expr;
            }
            if (Match(TokenType.SH))
            {
                Expr arg;
                // Allow both quoted and block forms.
                if (Check(TokenType.LBRACE))
                    arg = ParseShellBlock();
                else
                    arg = ParseExpression(); // expects a literal string like "ls -ltrha"
                return new ShellExpr(arg);
            }
            if (Match(TokenType.SSH))
            {
                var host = ParseExpression();
                Expr arg;

                // Allow both quoted and block forms.
                if (Check(TokenType.LBRACE))
                    arg = ParseShellBlock();
                else
                    arg = ParseExpression(); // expects a literal string like "ls -ltrha"
                return new SshExpr(host, arg);
            }
            if (Match(TokenType.LBRACE))
            {
                // Lookahead: if the next token (or tokens) match a dict key pattern,
                // then parse as dictionary; otherwise, assume it's a code block.
                if (IsDictionaryStart())
                {
                    return ParseDictionary();
                }
                else
                {
                    // If it's not a dictionary, then it must be a block.
                    // Depending on your DSL, you might handle inline blocks differently.
                    throw new ParsingException(
                        "ParsePrimary matched a {, did a lookahead check, decided it wasn't a dict and so wants to parse a block.  Does the grammar support this??",
                        Peek().Line,
                        Peek().Column
                    );
                }
            }
            /**/
            /*if (Match(TokenType.DOT)) {*/
            /*  return ParsePostfix(expr);*/
            /*}*/

            throw new ParsingException(
                $"Unexpected token '{Peek().Lexeme}' in expression at line " + Peek().Line,
                Peek().Line,
                Peek().Column
            );
        }

        private Expr ParseInterpolatedString()
        {
            List<Expr> parts = new List<Expr>();

            while (!Check(TokenType.INTERPOLATED_STRING) && !IsAtEnd())
            {
                if (Match(TokenType.STRING))
                {
                    parts.Add(new LiteralExpr(Previous().Lexeme));
                }
                else if (Match(TokenType.INTERPOLATION_EXPR))
                {
                    // Parse the expression inside the interpolation
                    Expr expr = ParseExpression();
                    parts.Add(expr);
                }
            }

            Consume(TokenType.INTERPOLATED_STRING, "Expected end of interpolated string.");

            // If we only have one part and it's a literal, simplify
            if (parts.Count == 1 && parts[0] is LiteralExpr)
            {
                return parts[0];
            }

            return new InterpolatedStringExpr(parts);
        }

        private Expr ParsePostfix(Expr expr)
        {
            // Loop to allow chaining, e.g. obj.method().prop.method2(), etc.
            while (true)
            {
                if (Match(TokenType.DOT))
                {
                    // Consume the member name
                    Token nameToken = Consume(
                        TokenType.IDENTIFIER,
                        "Expected property or method name after '.'"
                    );
                    expr = new MemberAccessExpr(expr, nameToken.Lexeme);

                    // If the next token is an LPAREN, then we are making a method call.
                    if (Match(TokenType.LPAREN))
                    {
                        var arguments = new List<Expr>();
                        if (!Check(TokenType.RPAREN))
                        {
                            do
                            {
                                arguments.Add(ParseExpression());
                            } while (Match(TokenType.COMMA));
                        }
                        Consume(TokenType.RPAREN, "Expected ')' after method call arguments");
                        expr = new CallExpr(expr, arguments);
                    }
                }
                else if (Match(TokenType.LPAREN))
                {
                    // In case a call immediately follows (without a preceding dot) – e.g. when calling a function literal.
                    var arguments = new List<Expr>();
                    if (!Check(TokenType.RPAREN))
                    {
                        do
                        {
                            arguments.Add(ParseExpression());
                        } while (Match(TokenType.COMMA));
                    }
                    Consume(TokenType.RPAREN, "Expected ')' after arguments");
                    expr = new CallExpr(expr, arguments);
                }
                else if (Match(TokenType.LBRACKET))
                {
                    Expr indexExpr = ParseExpression();
                    Consume(TokenType.RBRACKET, "Expected ']' after index expression");
                    expr = new IndexExpr(expr, indexExpr);
                }
                else
                {
                    break;
                }
            }
            return expr;
        }

        private bool IsDictionaryStart()
        {
            // If immediately closed, treat it as an empty dict.
            if (Check(TokenType.RBRACE))
                return true;
            Token potentialKey = Peek();
            if (
                (potentialKey.Type == TokenType.IDENTIFIER || potentialKey.Type == TokenType.STRING)
                && Peek(1).Type == TokenType.COLON
            )
                return true;
            return false;
        }

        private Expr ParseShellExpression()
        {
            // '{' already consumed
            int braceCount = 1;
            StringBuilder sb = new StringBuilder();
            while (!IsAtEnd() && braceCount > 0)
            {
                Token token = Advance();
                if (token.Type == TokenType.LBRACE)
                {
                    braceCount++;
                    sb.Append(token.Lexeme + " ");
                }
                else if (token.Type == TokenType.RBRACE)
                {
                    braceCount--;
                    if (braceCount == 0)
                        break;
                    sb.Append(token.Lexeme + " ");
                }
                else
                {
                    sb.Append(token.Lexeme + " ");
                }
            }
            return new LiteralExpr(sb.ToString().Trim());
        } // ---------- Utility ----------

        private bool Match(params TokenType[] types)
        {
            foreach (var t in types)
            {
                if (Check(t))
                {
                    Advance();
                    return true;
                }
            }
            return false;
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd())
                return false;
            return Peek().Type == type;
        }

        private bool CheckNext(TokenType type)
        {
            if (IsAtEnd() || _current + 1 >= _tokens.Count)
                return false;
            return _tokens[_current + 1].Type == type;
        }

        private Token Advance()
        {
            if (!IsAtEnd())
                _current++;
            return Previous();
        }

        private bool IsAtEnd() => Peek().Type == TokenType.EOF;

        private Token Peek(int n = 0) => _tokens[_current + n];

        private Token Previous() => _tokens[_current - 1];

        private Token Consume(TokenType type, string message)
        {
            if (Check(type))
                return Advance();
            throw new ParsingException(
                $"{message} (got token {type} on line {Peek().Line})",
                Peek().Line,
                Peek().Column
            );
        }
    }
}
