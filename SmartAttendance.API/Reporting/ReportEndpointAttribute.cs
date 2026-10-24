using System.Reflection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SmartAttendance.API.Reporting;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ReportEndpointAttribute(string summary, string description) : Attribute
{
    public string Summary { get; } = summary;
    public string Description { get; } = description;
}

public sealed class AttendanceReportOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var doc = context.MethodInfo.GetCustomAttribute<ReportEndpointAttribute>();
        if (doc is null) return;
        operation.Summary = doc.Summary;
        operation.Description = doc.Description + "\n\nPolicy closed-past-v1: confirmed enrollments only. " +
            "Only verified Closed meetings ending by AsOfUtc count. PresentSessions = Present + Late; " +
            "Excused is excluded for that student; missing marks remain eligible and never count as Present. " +
            "Open/future/cancelled/legacy meetings and inferred enrollments are excluded. " +
            "Zero denominator returns null. Overall is a ratio of summed counts. Date filters use meeting start in UTC. " +
            "Students are scoped to themselves, Teachers to assigned courses, Admin to administrative scope. " +
            "See GET /api/reports/policy for the complete machine-readable policy.";
        if (operation.Parameters is null) return;
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["studentId"] = "Student profile ID, never an account ID; limited to authorized students.",
            ["courseId"] = "Course ID; inaccessible and nonexistent resources return 404.",
            ["attendanceSessionId"] = "A specific class meeting ID; supports multiple meetings on one day.",
            ["fromDate"] = "Inclusive meeting-start calendar date in UTC (YYYY-MM-DD). Does not use marking time.",
            ["toDate"] = "Inclusive meeting-start calendar date in UTC (YYYY-MM-DD); must not precede fromDate.",
            ["includeLegacy"] = "Records only: include flagged legacy rows (default true). They never affect percentages; undated placeholders do not match date filters.",
            ["page"] = "One-based page number, default 1. An out-of-range page returns an empty items array.",
            ["pageSize"] = "Page size 1–100, default 25. TotalCount describes the filtered result before pagination.",
            ["sortBy"] = "Allowlisted sort field; stable ID tie-breakers are applied before pagination. Null percentages sort first ascending and last descending.",
            ["direction"] = "asc or desc, validated; default depends on endpoint query type.",
            ["status"] = "Records-only status filter; cannot narrow the percentage denominator.",
            ["method"] = "Records-only provenance label filter, not proof that recognition occurred.",
            ["teacherUserId"] = "Teacher account ID; Teacher can request only self, Admin can request a Teacher account."
        };
        foreach (var parameter in operation.Parameters)
            if (parameter.Name is not null && descriptions.TryGetValue(parameter.Name, out var description))
                parameter.Description = description;
    }
}
