using System.Globalization;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مُقيِّم صيغ عناصر الراتب الخادمي (نظير الاختبار الحيّ ببوابة المعادلة، لكن آمن
/// وبلا تنفيذ كود): يحلّل تعبيراً حسابياً على متغيّرات الموظف (Basic/Allowances/
/// Gross/Hours/Days/DailyRate/HourlyRate) ويرجع قيمة عشرية. القواعد: + − × ÷،
/// أقواس، سالب أحادي، والدوال ROUND(x[,n]) · MIN(..) · MAX(..) · ABS(x). أي رمز
/// غير معروف أو قسمة على صفر ⟶ خطأ (لا استثناء يُسقط المسير — يتخطّى المحرك العنصر).
/// محرك تحليل نزولي تعاودي بلا انعكاس ولا Function() — آمن للإدخال غير الموثوق.
/// </summary>
public static class SalaryFormulaEvaluator
{
    public static bool TryEvaluate(
        string? formula,
        IReadOnlyDictionary<string, decimal> variables,
        out decimal result,
        out string? error)
    {
        result = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(formula))
        {
            error = "صيغة فارغة.";
            return false;
        }

        try
        {
            var parser = new Parser(formula, variables);
            result = parser.ParseAndEvaluate();
            if (!IsFinite(result))
            {
                error = "ناتج غير صالح.";
                return false;
            }
            return true;
        }
        catch (FormulaException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsFinite(decimal _) => true; // decimal لا يحمل NaN/Infinity؛ القسمة على صفر تُعالَج صراحةً

    private sealed class FormulaException : Exception
    {
        public FormulaException(string message) : base(message) { }
    }

    /// <summary>محلّل نزولي تعاودي: تعبير ← حد ← عامل ← أوّلي (رقم/متغيّر/دالة/قوس).</summary>
    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, decimal> _vars;
        private int _pos;

        public Parser(string text, IReadOnlyDictionary<string, decimal> vars)
        {
            _text = text;
            _vars = vars;
        }

        public decimal ParseAndEvaluate()
        {
            var value = ParseExpression();
            SkipSpaces();
            if (_pos < _text.Length)
                throw new FormulaException($"رمز غير متوقّع عند الموضع {_pos + 1}.");
            return value;
        }

        // expr := term (('+' | '-') term)*
        private decimal ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipSpaces();
                var op = Peek();
                if (op == '+') { _pos++; value += ParseTerm(); }
                else if (op == '-') { _pos++; value -= ParseTerm(); }
                else break;
            }
            return value;
        }

        // term := factor (('*' | '/') factor)*
        private decimal ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipSpaces();
                var op = Peek();
                if (op == '*') { _pos++; value *= ParseFactor(); }
                else if (op == '/')
                {
                    _pos++;
                    var divisor = ParseFactor();
                    if (divisor == 0) throw new FormulaException("قسمة على صفر.");
                    value /= divisor;
                }
                else break;
            }
            return value;
        }

        // factor := ('+' | '-') factor | primary
        private decimal ParseFactor()
        {
            SkipSpaces();
            var c = Peek();
            if (c == '+') { _pos++; return ParseFactor(); }
            if (c == '-') { _pos++; return -ParseFactor(); }
            return ParsePrimary();
        }

        // primary := number | ident [ '(' args ')' ] | '(' expr ')'
        private decimal ParsePrimary()
        {
            SkipSpaces();
            var c = Peek();

            if (c == '(')
            {
                _pos++;
                var value = ParseExpression();
                Expect(')');
                return value;
            }

            if (char.IsDigit(c) || c == '.')
                return ParseNumber();

            if (char.IsLetter(c))
                return ParseIdentifier();

            throw new FormulaException(
                c == '\0' ? "نهاية غير متوقّعة للصيغة." : $"رمز غير صالح «{c}» عند الموضع {_pos + 1}.");
        }

        private decimal ParseNumber()
        {
            var start = _pos;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.')) _pos++;
            var token = _text[start.._pos];
            if (!decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                throw new FormulaException($"رقم غير صالح «{token}».");
            return value;
        }

        private decimal ParseIdentifier()
        {
            var start = _pos;
            while (_pos < _text.Length && char.IsLetter(_text[_pos])) _pos++;
            var name = _text[start.._pos];

            SkipSpaces();
            if (Peek() == '(')
            {
                _pos++;
                var args = ParseArguments();
                Expect(')');
                return ApplyFunction(name, args);
            }

            // متغيّر (غير حسّاس لحالة الأحرف)
            foreach (var kv in _vars)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;

            throw new FormulaException($"متغيّر غير معروف «{name}».");
        }

        private List<decimal> ParseArguments()
        {
            var args = new List<decimal>();
            SkipSpaces();
            if (Peek() == ')') return args; // بلا وسائط
            args.Add(ParseExpression());
            while (true)
            {
                SkipSpaces();
                if (Peek() == ',') { _pos++; args.Add(ParseExpression()); }
                else break;
            }
            return args;
        }

        private static decimal ApplyFunction(string name, List<decimal> args)
        {
            switch (name.ToUpperInvariant())
            {
                case "ROUND":
                    if (args.Count is 1)
                        return Math.Round(args[0], 0, MidpointRounding.AwayFromZero);
                    if (args.Count is 2)
                    {
                        var digits = (int)Math.Clamp(args[1], 0, 15);
                        return Math.Round(args[0], digits, MidpointRounding.AwayFromZero);
                    }
                    throw new FormulaException("ROUND تقبل وسيطاً أو وسيطين.");
                case "ABS":
                    if (args.Count != 1) throw new FormulaException("ABS تقبل وسيطاً واحداً.");
                    return Math.Abs(args[0]);
                case "MIN":
                    if (args.Count == 0) throw new FormulaException("MIN تحتاج وسيطاً واحداً على الأقل.");
                    return args.Min();
                case "MAX":
                    if (args.Count == 0) throw new FormulaException("MAX تحتاج وسيطاً واحداً على الأقل.");
                    return args.Max();
                default:
                    throw new FormulaException($"دالة غير معروفة «{name}».");
            }
        }

        private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

        private void SkipSpaces()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }

        private void Expect(char c)
        {
            SkipSpaces();
            if (Peek() != c) throw new FormulaException($"مطلوب «{c}» عند الموضع {_pos + 1}.");
            _pos++;
        }
    }
}
