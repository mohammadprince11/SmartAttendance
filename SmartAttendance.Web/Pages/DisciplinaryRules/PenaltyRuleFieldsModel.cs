namespace SmartAttendance.Web.Pages.DisciplinaryRules;

/// <summary>
/// ما يحتاجه جزء حقول قاعدة الجزاء: القاعدة نفسها (أو <c>null</c> لقاعدة جديدة)
/// وصفحتها لقراءة القوائم (بنود الاقتطاع · أسباب الإيقاف · القوالب).
///
/// جزءٌ واحد للإضافة والتعديل عمداً: نسختان من نفس الحقول تفترقان مع أول عمود
/// جديد — وهو بالضبط ما وقع بحقل «رقم الفقرة» فأعطب الحفظ والتعديل معاً.
/// </summary>
public sealed class PenaltyRuleFieldsModel
{
    public PenaltyRuleFieldsModel(IndexModel page, PenaltyRule? rule, int nextOccurrence = 1)
    {
        Page = page;
        Rule = rule;
        NextOccurrence = rule?.OccurrenceFrom ?? nextOccurrence;
    }

    public IndexModel Page { get; }

    public PenaltyRule? Rule { get; }

    /// <summary>رقم التكرار المقترح للقاعدة الجديدة — «المخالفة الخامسة» بكيان.</summary>
    public int NextOccurrence { get; }
}
