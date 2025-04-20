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
        SH,
        SSH,
        ONCE,
        EVERY,
        IF,
        ELSE,
        FOR,
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
        IDENTIFIER,
        EOF,

        FN,
        RETURN,
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
            while (!IsAtEnd())
            {
                _start = _current;
                ScanToken();
            }
            AddToken(TokenType.EOF, "<EOF>");
            //_tokens.Add(new Token(TokenType.EOF, "", _line, _column, _start));
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
            var sb = new StringBuilder();

            while (!IsAtEnd() && Peek() != '"')
            {
                sb.Append(Advance());
            }

            if (IsAtEnd())
                throw new Exception("Unterminated string at line " + _line);

            Advance(); // consume closing quote

            AddToken(TokenType.VERBATIM_STRING, sb.ToString());
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
            var sb = new StringBuilder();

            while (!IsAtEnd() && Peek() != '"')
            {
                if (hasEscapes && Peek() == '\\')
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

            // This is a regular STRING token for backward compatibility
            AddToken(TokenType.STRING, sb.ToString());
        }

        private void ProcessInterpolatedString()
        {
            // Mark the start of an interpolated string
            AddToken(TokenType.INTERPOLATED_STRING, "");

            var sb = new StringBuilder();
            bool inInterpolation = false;
            int braceCount = 0;
            int maxIterations = _source.Length; // Safeguard against infinite loops
            int iterations = 0;

            while (!IsAtEnd() && !(Peek() == '"' && !inInterpolation) && iterations < maxIterations)
            {
                iterations++;
                if (Peek() == '#' && Peek(1) == '{' && !inInterpolation)
                {
                    // Add accumulated string part
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
            var sb = new StringBuilder();
            sb.Append(first);

            while (!IsAtEnd() && IsDigit(Peek()))
            {
                sb.Append(Advance());
            }
            AddToken(TokenType.NUMBER, sb.ToString());
        }

        private void ScanIdentifierOrKeyword(char first)
        {
            var sb = new StringBuilder();
            sb.Append(first);

            while (!IsAtEnd() && (IsAlphaNumeric(Peek()) || Peek() == '_' || Peek() == '-'))
            {
                sb.Append(Advance());
            }

            string text = sb.ToString().ToLower();
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
                default:
                    AddToken(TokenType.IDENTIFIER, sb.ToString());
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
