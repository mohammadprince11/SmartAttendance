using System.Globalization;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مُقيِّم صيغ عناصر الراتب الخادمي (نظير الاختبار الحيّ ببوابة المعادلة، لكن آمن
/// وبلا تنفيذ كود): يحلّل تعبيراً حسابياً على متغيّرات الموظف (Basic/Allowances/
/// Gross/Hours/Days/DailyRate/HourlyRate) ويرجع قيمة عشرية. القواعد: + − × ÷،
/// أقواس، سالب أحادي، والدوال ROUND(x[,n]) · FLOOR(x) · CEIL(x) · MIN(..) ·
/// MAX(..) · ABS(x) · IF(شرط، صحّ، خطأ) · AND(..) · OR(..) · NOT(x)،
/// وعوامل المقارنة &lt; &gt; &lt;= &gt;= = &lt;&gt; (تُرجع 1 أو 0).
/// أي رمز غير معروف أو قسمة على صفر ⟶ خطأ (لا استثناء يُسقط المسير — يتخطّى المحرك العنصر).
/// محرك تحليل نزولي تعاودي بلا انعكاس ولا Function() — آمن للإدخال غير الموثوق.
///
/// ⚠️ <b>IF تُقيّم وسائطها الثلاثة جميعاً قبل الاختيار</b> (لا تقييم كسول): فرعٌ
/// غير مأخوذ يقسم على صفر يُفشل الصيغة كلّها. الحلّ بكتابة الصيغة:
/// <c>IF(Days &gt; 0, Basic / MAX(Days, 1), 0)</c>.
///
/// وإضافة المقارنة وIF **لا تغيّر ناتج أي صيغة قائمة**: صيغةٌ بلا رمز مقارنة
/// تمرّ بنفس المسار الحسابي السابق حرفياً.
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

        /// <summary>
        /// المستوى الأعلى: مقارنة اختيارية فوق الحساب.
        /// <c>comparison := sum (('&lt;' | '&gt;' | '&lt;=' | '&gt;=' | '=' | '&lt;&gt;') sum)?</c>
        ///
        /// المقارنة تُرجع 1 أو 0 لا منطقياً — فتُستعمل داخل <c>IF</c> وتُضرب مباشرةً
        /// (<c>Basic * (Basic &gt; 500000)</c>). وهي **إضافة محضة**: أي صيغة قائمة
        /// بلا رمز مقارنة تُقيَّم بنفس المسار السابق حرفياً وبنفس الناتج.
        /// </summary>
        private decimal ParseExpression()
        {
            var left = ParseSum();
            SkipSpaces();

            var op = ReadComparisonOperator();
            if (op is null)
            {
                return left;
            }

            var right = ParseSum();
            var comparison = left.CompareTo(right);

            return op switch
            {
                "<" => comparison < 0 ? 1 : 0,
                ">" => comparison > 0 ? 1 : 0,
                "<=" => comparison <= 0 ? 1 : 0,
                ">=" => comparison >= 0 ? 1 : 0,
                "=" => comparison == 0 ? 1 : 0,
                "<>" => comparison != 0 ? 1 : 0,
                _ => throw new FormulaException($"عامل مقارنة غير معروف «{op}».")
            };
        }

        private string? ReadComparisonOperator()
        {
            var c = Peek();
            if (c is not ('<' or '>' or '='))
            {
                return null;
            }

            _pos++;
            var next = Peek();

            if (c == '<' && next == '=') { _pos++; return "<="; }
            if (c == '<' && next == '>') { _pos++; return "<>"; }
            if (c == '>' && next == '=') { _pos++; return ">="; }

            return c.ToString();
        }

        // sum := term (('+' | '-') term)*
        private decimal ParseSum()
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
                // IF شرطية: بدونها لا تُكتب شريحة ضريبية ولا حدّ أدنى/أعلى مشروط،
                // وهما جوهر توطين الرواتب. الشرط أي تعبير: صفر = خطأ، وغيره = صحّ.
                case "IF":
                    if (args.Count != 3) throw new FormulaException("IF تقبل ثلاثة وسائط: IF(شرط، قيمة_صحّ، قيمة_خطأ).");
                    return args[0] != 0 ? args[1] : args[2];
                // FLOOR/CEIL: التقريب لأسفل/أعلى — لازمان بتقريب المستحقات لأقرب
                // دينار أو لأقرب ألف، ولا يغنيان عن ROUND (الذي يقرّب للأقرب).
                case "FLOOR":
                    if (args.Count != 1) throw new FormulaException("FLOOR تقبل وسيطاً واحداً.");
                    return Math.Floor(args[0]);
                case "CEIL":
                case "CEILING":
                    if (args.Count != 1) throw new FormulaException("CEIL تقبل وسيطاً واحداً.");
                    return Math.Ceiling(args[0]);
                // AND/OR كدالّتين لا كعاملَين: العامل يحتاج مستوى أولوية إضافياً
                // بالمحلّل، والدالّة تعطي نفس القدرة بلا لبس أسبقية.
                // أي وسيط غير صفري = صحّ، والناتج 1 أو 0 ليتّسق مع المقارنة.
                case "AND":
                    if (args.Count == 0) throw new FormulaException("AND تحتاج وسيطاً واحداً على الأقل.");
                    return args.All(value => value != 0) ? 1 : 0;
                case "OR":
                    if (args.Count == 0) throw new FormulaException("OR تحتاج وسيطاً واحداً على الأقل.");
                    return args.Any(value => value != 0) ? 1 : 0;
                case "NOT":
                    if (args.Count != 1) throw new FormulaException("NOT تقبل وسيطاً واحداً.");
                    return args[0] == 0 ? 1 : 0;
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
