using System;
using System.Collections.Generic;
using System.Globalization;

namespace CTEditor.GameDefinition.Domain.Formulas
{
    /// <summary>Error al leer una fórmula: dice QUÉ falló y EN QUÉ posición (para mostrarlo al autor).</summary>
    public sealed class MathExpressionException : Exception
    {
        public int Position { get; }
        public MathExpressionException(string message, int position)
            : base($"{message} (posición {position + 1})") => Position = position;
    }

    /// <summary>
    /// Una FÓRMULA MATEMÁTICA escrita por el autor, p. ej. "n^3", "0.8*n^3 + 100*n", "floor(n^3*(100-n)/50)".
    ///
    /// Es un pequeño intérprete hecho a mano (analizador de "descenso recursivo"): lee el texto una vez,
    /// construye un árbol de operaciones y luego lo evalúa todas las veces que haga falta, rápido.
    /// Es SEGURO: solo entiende aritmética, variables y un puñado de funciones; no puede ejecutar código.
    ///
    /// Sintaxis:
    ///   números 12  3.5     variables  n (o las que se permitan)     constantes  pi  e
    ///   operadores  + - * / %  ^ (potencia)  y paréntesis
    ///   funciones  floor ceil round abs sqrt exp ln log10 min(a,b) max(a,b) pow(a,b) clamp(x,a,b)
    ///   (en español también: piso techo redondear raiz). Separador de argumentos: ',' o ';'.
    ///
    /// Vive en el DOMINIO (matemática pura, sin Unity) para reutilizarlo: hoy en curvas de XP, mañana en
    /// fórmulas de captura, daño o experiencia definidas por el autor.
    /// </summary>
    public sealed class MathExpression
    {
        public string Source { get; }
        private readonly Node _root;

        private MathExpression(string source, Node root)
        {
            Source = source;
            _root = root;
        }

        /// <summary>Lee una fórmula. 'allowedVariables' = nombres válidos (null = cualquiera). Lanza si hay error.</summary>
        public static MathExpression Parse(string source, IEnumerable<string> allowedVariables = null)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new MathExpressionException("La fórmula está vacía", 0);
            var allowed = allowedVariables == null ? null : new HashSet<string>(allowedVariables, StringComparer.OrdinalIgnoreCase);
            var parser = new Parser(source, allowed);
            var root = parser.ParseAll();
            return new MathExpression(source, root);
        }

        /// <summary>Como Parse, pero sin excepciones: devuelve false y el mensaje de error para mostrarlo.</summary>
        public static bool TryParse(string source, IEnumerable<string> allowedVariables, out MathExpression expression, out string error)
        {
            try
            {
                expression = Parse(source, allowedVariables);
                error = null;
                return true;
            }
            catch (MathExpressionException e)
            {
                expression = null;
                error = e.Message;
                return false;
            }
        }

        /// <summary>Evalúa con una sola variable (el caso típico: n = nivel).</summary>
        public double Evaluate(string variable, double value)
            => _root.Eval(name => string.Equals(name, variable, StringComparison.OrdinalIgnoreCase) ? value : double.NaN);

        /// <summary>Evalúa con varias variables.</summary>
        public double Evaluate(IReadOnlyDictionary<string, double> variables)
            => _root.Eval(name => variables != null && variables.TryGetValue(name, out var v) ? v : double.NaN);

        // =====================================================================================
        // Árbol de la expresión: cada nodo sabe calcularse. (Patrón "Interpreter".)
        // =====================================================================================

        private abstract class Node { public abstract double Eval(Func<string, double> vars); }

        private sealed class Num : Node
        {
            private readonly double _v; public Num(double v) => _v = v;
            public override double Eval(Func<string, double> vars) => _v;
        }

        private sealed class Var : Node
        {
            private readonly string _name; public Var(string name) => _name = name;
            public override double Eval(Func<string, double> vars) => vars(_name);
        }

        private sealed class Unary : Node
        {
            private readonly Node _a; public Unary(Node a) => _a = a;
            public override double Eval(Func<string, double> vars) => -_a.Eval(vars);
        }

        private sealed class Binary : Node
        {
            private readonly char _op; private readonly Node _a, _b;
            public Binary(char op, Node a, Node b) { _op = op; _a = a; _b = b; }
            public override double Eval(Func<string, double> vars)
            {
                double a = _a.Eval(vars), b = _b.Eval(vars);
                switch (_op)
                {
                    case '+': return a + b;
                    case '-': return a - b;
                    case '*': return a * b;
                    case '/': return a / b;
                    case '%': return a % b;
                    default:  return Math.Pow(a, b); // '^'
                }
            }
        }

        private sealed class Call : Node
        {
            private readonly Func<double[], double> _fn; private readonly Node[] _args;
            public Call(Func<double[], double> fn, Node[] args) { _fn = fn; _args = args; }
            public override double Eval(Func<string, double> vars)
            {
                var values = new double[_args.Length];
                for (int i = 0; i < _args.Length; i++) values[i] = _args[i].Eval(vars);
                return _fn(values);
            }
        }

        // Funciones disponibles: nombre -> (nº de argumentos, implementación).
        private static readonly Dictionary<string, (int arity, Func<double[], double> fn)> Functions =
            new Dictionary<string, (int, Func<double[], double>)>(StringComparer.OrdinalIgnoreCase)
            {
                ["floor"] = (1, a => Math.Floor(a[0])),      ["piso"] = (1, a => Math.Floor(a[0])),
                ["ceil"] = (1, a => Math.Ceiling(a[0])),     ["techo"] = (1, a => Math.Ceiling(a[0])),
                ["round"] = (1, a => Math.Round(a[0], MidpointRounding.AwayFromZero)),
                ["redondear"] = (1, a => Math.Round(a[0], MidpointRounding.AwayFromZero)),
                ["abs"] = (1, a => Math.Abs(a[0])),
                ["sqrt"] = (1, a => Math.Sqrt(a[0])),        ["raiz"] = (1, a => Math.Sqrt(a[0])),
                ["exp"] = (1, a => Math.Exp(a[0])),
                ["ln"] = (1, a => Math.Log(a[0])),           ["log10"] = (1, a => Math.Log10(a[0])),
                ["min"] = (2, a => Math.Min(a[0], a[1])),    ["max"] = (2, a => Math.Max(a[0], a[1])),
                ["pow"] = (2, a => Math.Pow(a[0], a[1])),
                ["clamp"] = (3, a => Math.Max(a[1], Math.Min(a[2], a[0]))),
                // paso(x, umbral) = 1 si x >= umbral; si no, 0. Para tablas por tramos (Patada Baja: por peso).
                ["paso"] = (2, a => a[0] >= a[1] ? 1 : 0),    ["step"] = (2, a => a[0] >= a[1] ? 1 : 0),
                // si(condición, a, b) = a si la condición no es 0; si no, b.
                ["si"] = (3, a => a[0] != 0 ? a[1] : a[2]),   ["if"] = (3, a => a[0] != 0 ? a[1] : a[2]),
            };

        /// <summary>Lista legible de funciones (para la ayuda del editor).</summary>
        public static IEnumerable<string> FunctionNames => Functions.Keys;

        // =====================================================================================
        // Analizador (descenso recursivo). Cada método reconoce un nivel de precedencia:
        //   expr  := term (('+'|'-') term)*
        //   term  := unary (('*'|'/'|'%') unary)*
        //   unary := ('-'|'+') unary | power
        //   power := primary ('^' unary)?        (asociativa a la derecha: 2^3^2 = 2^9)
        //   primary := número | nombre | nombre '(' args ')' | '(' expr ')'
        // =====================================================================================

        private sealed class Parser
        {
            private readonly string _s;
            private readonly HashSet<string> _allowed;
            private int _i;

            public Parser(string s, HashSet<string> allowed) { _s = s; _allowed = allowed; }

            public Node ParseAll()
            {
                var node = Expr();
                Skip();
                if (_i < _s.Length) throw new MathExpressionException($"No esperaba '{_s[_i]}'", _i);
                return node;
            }

            private Node Expr()
            {
                var left = Term();
                while (true)
                {
                    Skip();
                    if (Peek('+')) { _i++; left = new Binary('+', left, Term()); }
                    else if (Peek('-')) { _i++; left = new Binary('-', left, Term()); }
                    else return left;
                }
            }

            private Node Term()
            {
                var left = UnaryExpr();
                while (true)
                {
                    Skip();
                    char c = _i < _s.Length ? _s[_i] : '\0';
                    if (c == '*' || c == '/' || c == '%') { _i++; left = new Binary(c, left, UnaryExpr()); }
                    else return left;
                }
            }

            private Node UnaryExpr()
            {
                Skip();
                if (Peek('-')) { _i++; return new Unary(UnaryExpr()); }
                if (Peek('+')) { _i++; return UnaryExpr(); }
                return Power();
            }

            private Node Power()
            {
                var b = Primary();
                Skip();
                if (Peek('^')) { _i++; return new Binary('^', b, UnaryExpr()); }
                return b;
            }

            private Node Primary()
            {
                Skip();
                if (_i >= _s.Length) throw new MathExpressionException("La fórmula termina antes de tiempo", _i);
                char c = _s[_i];

                if (c == '(')
                {
                    _i++;
                    var inner = Expr();
                    Skip();
                    if (!Peek(')')) throw new MathExpressionException("Falta cerrar un paréntesis ')'", _i);
                    _i++;
                    return inner;
                }

                if (char.IsDigit(c) || c == '.') return Number();
                if (char.IsLetter(c) || c == '_') return NameOrCall();

                throw new MathExpressionException($"No esperaba '{c}'", _i);
            }

            private Node Number()
            {
                int start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                string text = _s.Substring(start, _i - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    throw new MathExpressionException($"Número mal escrito '{text}' (usa punto para decimales: 1.5)", start);
                return new Num(v);
            }

            private Node NameOrCall()
            {
                int start = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                string name = _s.Substring(start, _i - start);
                Skip();

                if (Peek('('))
                {
                    if (!Functions.TryGetValue(name, out var f))
                        throw new MathExpressionException($"Función desconocida '{name}'", start);
                    _i++;
                    var args = new List<Node>();
                    Skip();
                    if (!Peek(')'))
                    {
                        while (true)
                        {
                            args.Add(Expr());
                            Skip();
                            if (Peek(',') || Peek(';')) { _i++; continue; }
                            break;
                        }
                    }
                    Skip();
                    if (!Peek(')')) throw new MathExpressionException($"Falta ')' al final de {name}(...)", _i);
                    _i++;
                    if (args.Count != f.arity)
                        throw new MathExpressionException($"{name} necesita {f.arity} valor(es) y recibió {args.Count}", start);
                    return new Call(f.fn, args.ToArray());
                }

                if (string.Equals(name, "pi", StringComparison.OrdinalIgnoreCase)) return new Num(Math.PI);
                if (string.Equals(name, "e", StringComparison.OrdinalIgnoreCase)) return new Num(Math.E);
                if (_allowed != null && !_allowed.Contains(name))
                    throw new MathExpressionException($"Variable desconocida '{name}' (puedes usar: {string.Join(", ", _allowed)})", start);
                return new Var(name);
            }

            private bool Peek(char c) => _i < _s.Length && _s[_i] == c;
            private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        }
    }
}
