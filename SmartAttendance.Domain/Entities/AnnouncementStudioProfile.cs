namespace SmartAttendance.Domain.Entities;

public class AnnouncementStudioProfile
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Key { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public class AnnouncementStudioDesign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int CompanyId { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "image/png";
    public byte[] Data { get; set; } = [];
    public bool IsActive { get; set; } = true;
}
