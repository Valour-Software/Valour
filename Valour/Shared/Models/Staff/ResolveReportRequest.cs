namespace Valour.Shared.Models.Staff;

public class ResolveReportRequest
{
    public string ReportId { get; set; }
    public ReportResolution Resolution { get; set; }
    public string StaffNotes { get; set; }
}
