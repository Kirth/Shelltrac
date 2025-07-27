using System;
using System.Collections.Generic;
using System.Text;

namespace Shelltrac
{
    public enum TokenType
    {
        // Keywords
        TASK,
        ON,
        EMIT,
        TRIGGER,
        DO,
        LOG,
        ECHO,
        SH,
        SSH,
        ONCE,
        EVERY,
        IF,
        ELSE,
        FOR,
        WHILE,
        PARALLEL,
        CANCEL,
        OVERRIDE,
        LET,
        TRUE,
        FALSE,
        UNDERSCORE,
        AT,

        // Operators / punctuation
        DOT,
        LBRACE,
        RBRACE,
        LPAREN,
        RPAREN,
        LBRACKET,
        RBRACKET,
        COLON,
        COMMA,
        SEMICOLON,
        ARROW, // =>
        EQUAL, // =
        DOTDOT, // ..
        PLUS, // +
        STAR, // *
        SLASH, // /
        MINUS, // -
        LESS, // <
        EQUAL_EQUAL, // ==
        NOT_EQUAL, // !=
        LESS_EQUAL, // <=
        GREATER, // >
        GREATER_EQUAL, // >=
        NOT,

        // String-related tokens
        STRING,

        // Additional tokens for interpolation
        INTERPOLATED_STRING, // Marker for a string that contains interpolation
        INTERPOLATION_EXPR, // An expression within an interpolated string

        VERBATIM_STRING, // For @"..." strings with no interpolation
        NUMBER,
        TIME_LITERAL, // For time literals like 5m, 30s, 1h
        IDENTIFIER,
        EOF,

        FN,
        RETURN,

        PARSE,
        WITH,
        AS,
    }

    public class Token
    {
        public TokenType Type { get; }
        public string Lexeme { get; }
        public int Line { get; }
        public int Column { get; }
        public int StartIndex { get; }
        public int Length { get; }

        public Token(TokenType type, string lexeme, int line, int column, int startidx, int length)
        {
            Type = type;
            Lexeme = lexeme;
            Line = line;
            Column = column;
            StartIndex = startidx;
            Length = length;
        }

        public override string ToString() => $"{Type} '{Lexeme}' (line {Line})";
    }

    public class DSLScanner
    {
        private readonly string _source;
        private readonly List<Token> _tokens = new();
        private int _start;
        private int _current;
        private int _line;
        private int _column;

        public DSLScanner(string source)
        {
            _source = source;
            _line = 1;
        }

        public List<Token> ScanTokens()
        {
#if PERF_TRACKING
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            var tokenCount = 0;
            var stringTokens = 0;
            var identifierTokens = 0;
            var numberTokens = 0;
            var stringSw = new System.Diagnostics.Stopwatch();
            var identifierSw = new System.Diagnostics.Stopwatch();
            var numberSw = new System.Diagnostics.Stopwatch();
#endif
            
            while (!IsAtEnd())
            {
                _start = _current;
#if PERF_TRACKING
                char c = _source[_current];
                
                if (c == '"' || c == '@')
                {
                    stringSw.Start();
                    ScanToken();
                    stringSw.Stop();
                    stringTokens++;
                }
                else if (IsDigit(c))
                {
                    numberSw.Start();
                    ScanToken();
                    numberSw.Stop();
                    numberTokens++;
                }
                else if (IsAlpha(c))
                {
                    identifierSw.Start();
                    ScanToken();
                    identifierSw.Stop();
                    identifierTokens++;
                }
                else
                {
                    ScanToken();
                }
                tokenCount++;
#else
                ScanToken();
#endif
            }
            AddToken(TokenType.EOF, "<EOF>");
            
#if PERF_TRACKING
            totalSw.Stop();
            Console.WriteLine($"[PERF] Scanning breakdown - Total: {totalSw.ElapsedMilliseconds}ms");
            Console.WriteLine($"[PERF]   Strings: {stringTokens} tokens, {stringSw.ElapsedMilliseconds}ms");
            Console.WriteLine($"[PERF]   Identifiers: {identifierTokens} tokens, {identifierSw.ElapsedMilliseconds}ms");
            Console.WriteLine($"[PERF]   Numbers: {numberTokens} tokens, {numberSw.ElapsedMilliseconds}ms");
            Console.WriteLine($"[PERF]   Other: {tokenCount - stringTokens - identifierTokens - numberTokens} tokens");
#endif
            
            return _tokens;
        }

        private void ScanToken()
        {
            char c = Advance();
            _column++;
            switch (c)
            {
                case ' ':
                case '\r':
                case '\t':
                    break;
                case '\n':
                    _line++;
                    _column = 0;
                    break;
                case '(':
                    AddToken(TokenType.LPAREN, "(");
                    break;
                case ',':
                    AddToken(TokenType.COMMA, ",");
                    break;
                case ':':
                    AddToken(TokenType.COLON, ":");
                    break;
                case ';':
                    AddToken(TokenType.SEMICOLON, ";");
                    break;
                case ')':
                    AddToken(TokenType.RPAREN, ")");
                    break;
                case '[':
                    AddToken(TokenType.LBRACKET, "[");
                    break;
                case ']':
                    AddToken(TokenType.RBRACKET, "]");
                    break;
                case '{':
                    AddToken(TokenType.LBRACE, "{");
                    break;
                case '}':
                    AddToken(TokenType.RBRACE, "}");
                    break;
                case '_':
                    AddToken(TokenType.UNDERSCORE, "_");
                    break;
                case '@':
                    if (Match('"'))
                        ScanVerbatimString();
                    else
                        AddToken(TokenType.AT, "@");
                    break;
                case '=':
                    if (Match('='))
                        AddToken(TokenType.EQUAL_EQUAL, "==");
                    else if (Match('>'))
                        AddToken(TokenType.ARROW, "=>");
                    else
                        AddToken(TokenType.EQUAL, "=");
                    break;

                case '!':
                    if (Match('='))
                        AddToken(TokenType.NOT_EQUAL, "!=");
                    else
                        AddToken(TokenType.NOT, "!");
                    break;

                case '<':
                    if (Match('='))
                        AddToken(TokenType.LESS_EQUAL, "<=");
                    else
                        AddToken(TokenType.LESS, "<");
                    break;

                case '>':
                    if (Match('='))
                        AddToken(TokenType.GREATER_EQUAL, ">=");
                    else
                        AddToken(TokenType.GREATER, ">");
                    break;

                case '/':
                    AddToken(TokenType.SLASH, "/");
                    break;
                case '*':
                    AddToken(TokenType.STAR, "*");
                    break;
                case '-':
                    if (Match('>'))
                    {
                        AddToken(TokenType.ARROW, "->");
                    }
                    else
                    {
                        AddToken(TokenType.MINUS, "-");
                    }
                    break;
                case '+':
                    AddToken(TokenType.PLUS, "+");
                    break;
                case '.':
                    if (Match('.'))
                        AddToken(TokenType.DOTDOT, "..");
                    else
                        AddToken(TokenType.DOT, ".");
                    break;
                case '"':
                    ScanString();
                    break;
                case '#':
                    while (!IsAtEnd() && Peek() != '\n')
                    {
                        Advance();
                    }
                    break;
                default:
                    if (IsDigit(c))
                    {
                        ScanNumber(c);
                    }
                    else if (IsAlpha(c))
                    {
                        ScanIdentifierOrKeyword(c);
                    }
                    // else ignore unrecognized chars
                    break;
            }
        }

        private void ScanVerbatimString()
        {
            int startPos = _current;
            
            while (!IsAtEnd() && Peek() != '"')
            {
                Advance();
            }

            if (IsAtEnd())
                throw new Exception("Unterminated string at line " + _line);

            // Extract the string span directly without StringBuilder
            var stringSpan = _source.AsSpan(startPos, _current - startPos);
            Advance(); // consume closing quote

            AddToken(TokenType.VERBATIM_STRING, stringSpan.ToString());
        }

        private void ScanString()
        {
            // First, capture the entire string to check for interpolation
            int startPos = _current;
            bool hasInterpolation = false;
            bool hasEscapeSequence = false;

            while (!IsAtEnd() && Peek() != '"')
            {
                if (Peek() == '#' && Peek(1) == '{')
                {
                    hasInterpolation = true;
                }

                if (Peek() == '\\')
                {
                    hasEscapeSequence = true;
                    Advance(); // Skip the backslash
                }

                Advance();
            }

            if (IsAtEnd())
                throw new Exception("Unterminated string at line " + _line);

            Advance(); // consume closing quote

            // Reset position to after the opening quote
            _current = startPos;

            if (hasInterpolation)
            {
                // Process as interpolated string
                ProcessInterpolatedString();
            }
            else
            {
                // Process as regular string, handling escapes if needed
                ProcessRegularString(hasEscapeSequence);
            }
        }

        private void ProcessRegularString(bool hasEscapes)
        {
            if (!hasEscapes)
            {
                // Fast path for strings without escapes - use span
                int startPos = _current;
                while (!IsAtEnd() && Peek() != '"')
                {
                    Advance();
                }

                if (IsAtEnd())
                    throw new Exception("Unterminated string at line " + _line);

                // Extract the string span directly without StringBuilder
                var stringSpan = _source.AsSpan(startPos, _current - startPos);
                Advance(); // consume closing quote
                AddToken(TokenType.STRING, stringSpan.ToString());
                return;
            }

            // Slow path for strings with escapes
            var sb = new StringBuilder();

            while (!IsAtEnd() && Peek() != '"')
            {
                if (Peek() == '\\')
                {
                    Advance(); // consume backslash
                    char next = Advance();

                    // Handle escape sequences
                    switch (next)
                    {
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '"':
                            sb.Append('"');
                            break;
                        case '#':
                            sb.Append('#');
                            break;
                        default:
                            sb.Append(next);
                            break;
                    }
                }
                else
                {
                    sb.Append(Advance());
                }
            }

            if (IsAtEnd())
                throw new Exception("Unterminated string at line " + _line);

            Advance(); // consume closing quote
            AddToken(TokenType.STRING, sb.ToString());
        }

        private void ProcessInterpolatedString()
        {
            // Mark the start of an interpolated string
            AddToken(TokenType.INTERPOLATED_STRING, "");

            var sb = new StringBuilder(256); // Pre-allocate reasonable capacity
            bool inInterpolation = false;
            int braceCount = 0;
            int maxIterations = _source.Length; // Safeguard against infinite loops
            int iterations = 0;

            while (!IsAtEnd() && !(Peek() == '"' && !inInterpolation) && iterations < maxIterations)
            {
                iterations++;
                if (Peek() == '#' && Peek(1) == '{' && !inInterpolation)
                {
                    // Add accumulated string part using span if possible
                    if (sb.Length > 0)
                    {
                        AddToken(TokenType.STRING, sb.ToString());
                        sb.Clear();
                    }

                    Advance(); // consume #
                    Advance(); // consume {
                    inInterpolation = true;
                    braceCount = 1;

                    // Mark the start of an interpolated expression
                    AddToken(TokenType.INTERPOLATION_EXPR, "");

                    // Capture the expression
                    int exprStart = _current;

                    // Track nested braces to handle complex expressions
                    while (!IsAtEnd() && !(Peek() == '}' && braceCount == 1))
                    {
                        if (Peek() == '{')
                        {
                            braceCount++;
                        }
                        else if (Peek() == '}')
                        {
                            braceCount--;
                        }
                        Advance();
                    }

                    if (IsAtEnd() || Peek() != '}')
                        throw new Exception("Unterminated interpolation at line " + _line);

                    // Get the expression content
                    string expr = _source.Substring(exprStart, _current - exprStart);

                    // Tokenize the expression and add its tokens
                    var exprScanner = new DSLScanner(expr);
                    var exprTokens = exprScanner.ScanTokens();

                    // Skip the EOF token from the inner scanner
                    for (int i = 0; i < exprTokens.Count - 1; i++)
                    {
                        _tokens.Add(exprTokens[i]);
                    }

                    Advance(); // consume }
                    inInterpolation = false;
                    braceCount = 0;
                }
                else if (!inInterpolation && Peek() == '\\')
                {
                    Advance(); // consume backslash
                    char next = Advance();

                    // Handle escape sequences
                    switch (next)
                    {
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '"':
                            sb.Append('"');
                            break;
                        case '#':
                            sb.Append('#');
                            break;
                        default:
                            sb.Append(next);
                            break;
                    }
                }
                else
                {
                    // For simple characters in interpolated strings, just advance and append
                    sb.Append(Advance());
                }
            }

            if (iterations >= maxIterations)
            {
                throw new Exception(
                    "Possible infinite loop detected in interpolated string processing at line "
                        + _line
                );
            }

            // Add final string part if any
            if (sb.Length > 0)
            {
                AddToken(TokenType.STRING, sb.ToString());
            }

            if (IsAtEnd())
                throw new Exception("Unterminated string at line " + _line);

            Advance(); // consume closing quote

            // Mark the end of the interpolated string
            AddToken(TokenType.INTERPOLATED_STRING, "");
        }

        private void ScanNumber(char first)
        {
            int startPos = _current - 1; // Include the first character

            while (!IsAtEnd() && IsDigit(Peek()))
            {
                Advance();
            }
            
            // Fast path: check for time suffixes without string allocations
            if (!IsAtEnd() && IsAlpha(Peek()))
            {
                int timeSuffixStart = _current;
                char firstSuffixChar = Peek();
                
                // Quick check for common time suffixes without creating strings
                if (firstSuffixChar == 'm' || firstSuffixChar == 's' || firstSuffixChar == 'h' || firstSuffixChar == 'd')
                {
                    Advance(); // consume first suffix char
                    
                    bool isValidTimeSuffix = false;
                    if (firstSuffixChar == 's' && IsAtEnd() || !IsAlpha(Peek()))
                    {
                        isValidTimeSuffix = true; // "s"
                    }
                    else if (firstSuffixChar == 'm')
                    {
                        if (Peek() == 's') // "ms"
                        {
                            Advance();
                            isValidTimeSuffix = true;
                        }
                        else if (IsAtEnd() || !IsAlpha(Peek())) // "m"
                        {
                            isValidTimeSuffix = true;
                        }
                    }
                    else if ((firstSuffixChar == 'h' || firstSuffixChar == 'd') && (IsAtEnd() || !IsAlpha(Peek())))
                    {
                        isValidTimeSuffix = true; // "h" or "d"
                    }
                    
                    if (isValidTimeSuffix)
                    {
                        // Extract the full time literal span
                        var timeLiteralSpan = _source.AsSpan(startPos, _current - startPos);
                        AddToken(TokenType.TIME_LITERAL, timeLiteralSpan.ToString());
                        return;
                    }
                    else
                    {
                        // Not a valid time suffix, rewind
                        _current = timeSuffixStart;
                    }
                }
                else
                {
                    // Not a time suffix starting character, continue scanning identifier if needed
                    while (!IsAtEnd() && IsAlpha(Peek()))
                    {
                        Advance();
                    }
                    // Rewind - this is not a time literal
                    _current = timeSuffixStart;
                }
            }
            
            // Extract the number span directly
            var numberSpan = _source.AsSpan(startPos, _current - startPos);
            AddToken(TokenType.NUMBER, numberSpan.ToString());
        }

        private void ScanIdentifierOrKeyword(char first)
        {
            int startPos = _current - 1; // Include the first character

            while (!IsAtEnd() && (IsAlphaNumeric(Peek()) || Peek() == '_' || Peek() == '-'))
            {
                Advance();
            }

            // Extract the identifier span directly
            var identifierSpan = _source.AsSpan(startPos, _current - startPos);
            string text = identifierSpan.ToString().ToLower();
            switch (text)
            {
                case "task":
                    AddToken(TokenType.TASK, text);
                    break;
                case "on":
                    AddToken(TokenType.ON, text);
                    break;
                    ;
                case "trigger":
                    AddToken(TokenType.TRIGGER, text);
                    break;
                case "emit":
                    AddToken(TokenType.EMIT, text);
                    break;
                case "do":
                    AddToken(TokenType.DO, text);
                    break;
                case "log":
                    AddToken(TokenType.LOG, text);
                    break;
                case "echo":
                    AddToken(TokenType.ECHO, text);
                    break;
                case "sh":
                    AddToken(TokenType.SH, text);
                    break;
                case "ssh":
                    AddToken(TokenType.SSH, text);
                    break;
                case "once":
                    AddToken(TokenType.ONCE, text);
                    break;
                case "every":
                    AddToken(TokenType.EVERY, text);
                    break;
                case "if":
                    AddToken(TokenType.IF, text);
                    break;
                case "else":
                    AddToken(TokenType.ELSE, text);
                    break;
                case "parallel":
                    AddToken(TokenType.PARALLEL, text);
                    break;
                case "cancel":
                    AddToken(TokenType.CANCEL, text);
                    break;
                case "override":
                    AddToken(TokenType.OVERRIDE, text);
                    break;
                case "for":
                    AddToken(TokenType.FOR, text);
                    break;
                case "while":
                    AddToken(TokenType.WHILE, text);
                    break;
                case "let":
                    AddToken(TokenType.LET, text);
                    break;
                case "true":
                    AddToken(TokenType.TRUE, text);
                    break;
                case "false":
                    AddToken(TokenType.FALSE, text);
                    break;
                case "fn":
                    AddToken(TokenType.FN, text);
                    break;
                case "return":
                    AddToken(TokenType.RETURN, text);
                    break;
                case "parse":
                    AddToken(TokenType.PARSE, text);
                    break;
                case "with":
                    AddToken(TokenType.WITH, text);
                    break;
                case "as":
                    AddToken(TokenType.AS, text);
                    break;
                default:
                    AddToken(TokenType.IDENTIFIER, identifierSpan.ToString());
                    break;


            }
        }

        private bool IsDigit(char c) => char.IsDigit(c);

        private bool IsAlpha(char c) => char.IsLetter(c);

        private bool IsAlphaNumeric(char c) => char.IsLetterOrDigit(c);

        private char Peek(int n = 0)
        {
            if (IsAtEnd())
                return '\0';
            return _source[_current + n];
        }

        private bool Match(char expected)
        {
            if (IsAtEnd())
                return false;
            if (_source[_current] != expected)
                return false;
            _current++;
            return true;
        }

        private char Advance()
        {
            _current++;
            return _source[_current - 1];
        }

        private bool IsAtEnd() => _current >= _source.Length;

        private void AddToken(TokenType type, string lexeme)
        {
            int length = _current - _start;
            _tokens.Add(new Token(type, lexeme, _line, _column, _start, length));
        }
    }
}
