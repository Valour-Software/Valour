namespace Valour.Shared.Models.Staff;

public class ReportQueryRequestFilter
{
    public ReportReasonCode? Reason { get; set; }
}

public class ReportQueryRequestSort
{
    public string Field { get; set; }
    public bool Descending { get; set; }
}

public class ReportQueryModel : QueryModel
{
    public override string GetApiUrl()
        => "api/staff/reports/query";

    public ReportQueryRequestFilter Filter { get; set; }
    public ReportQueryRequestSort Sort { get; set; }
}