namespace SmartAttendance.Domain.Enums;

/// <summary>
/// Relation of an employee's family member / dependent, matching the split Kayan
/// uses (spouse / children / relatives) under a single record shape.
/// </summary>
public enum DependentRelation
{
    Spouse = 1,
    Son = 2,
    Daughter = 3,
    Relative = 4
}
