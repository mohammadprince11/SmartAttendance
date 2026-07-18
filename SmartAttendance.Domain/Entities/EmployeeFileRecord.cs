using SmartAttendance.Domain.Common;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Domain.Entities;

/// <summary>
/// A structured 360° record inside the "ملفات الموظف" tab (education, experience,
/// certificate, training, medical). One uniform shape for every category — like the
/// dependents block — with an optional file attachment. Type-specific meaning is
/// carried by <see cref="RecordType"/>; the UI labels the shared fields per type.
/// </summary>
public class EmployeeFileRecord : AuditableEntity
{
    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public EmployeeRecordType RecordType { get; set; }

    /// <summary>Primary label (university / company / certificate / course / condition).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Secondary detail (degree+major / job title / …).</summary>
    public string? Subtitle { get; set; }

    public string? Country { get; set; }

    /// <summary>Reference / licence number, or cost for training.</summary>
    public string? RefNo { get; set; }

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public string? Note { get; set; }

    /// <summary>Optional uploaded file (original name + served path).</summary>
    public string? AttachmentName { get; set; }

    public string? AttachmentPath { get; set; }
}
