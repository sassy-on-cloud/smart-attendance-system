using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace SmartAttendance.API.DTOs;

public class AttendanceReportFilter : IValidatableObject
{
    [FromQuery(Name = "studentId"), Range(1, int.MaxValue)]
    public int? StudentId { get; set; }
    [FromQuery(Name = "courseId"), Range(1, int.MaxValue)]
    public int? CourseId { get; set; }
    [FromQuery(Name = "attendanceSessionId"), Range(1, int.MaxValue)]
    public int? AttendanceSessionId { get; set; }
    [FromQuery(Name = "fromDate")]
    public DateOnly? FromDate { get; set; }
    [FromQuery(Name = "toDate")]
    public DateOnly? ToDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
            yield return new ValidationResult("fromDate must not follow toDate.", [nameof(FromDate), nameof(ToDate)]);
        if (ToDate == DateOnly.MaxValue)
            yield return new ValidationResult("toDate must precede 9999-12-31.", [nameof(ToDate)]);
    }
}

public class AttendanceRecordQuery : AttendanceReportFilter
{
    [FromQuery(Name = "status"), RegularExpression("^(Present|Absent|Late|Excused|Unverified)$")]
    public string? Status { get; set; }
    [FromQuery(Name = "method"), RegularExpression("^(Manual|FaceRecognition|LegacyUnknown)$")]
    public string? Method { get; set; }
    [FromQuery(Name = "includeLegacy")]
    public bool IncludeLegacy { get; set; } = true;
    [FromQuery(Name = "page"), Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;
    [FromQuery(Name = "pageSize"), Range(1, 100)]
    public int PageSize { get; set; } = 25;
    [FromQuery(Name = "sortBy"), Required, RegularExpression("^(sessionDate|studentId|courseId|status|recordedAt)$")]
    public string SortBy { get; set; } = "sessionDate";
    [FromQuery(Name = "direction"), Required, RegularExpression("^(asc|desc)$")]
    public string Direction { get; set; } = "desc";
}

public class AttendancePercentageQuery : AttendanceReportFilter
{
    [FromQuery(Name = "page"), Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;
    [FromQuery(Name = "pageSize"), Range(1, 100)]
    public int PageSize { get; set; } = 25;
    [FromQuery(Name = "sortBy"), Required, RegularExpression("^(studentId|courseId|percentage)$")]
    public string SortBy { get; set; } = "courseId";
    [FromQuery(Name = "direction"), Required, RegularExpression("^(asc|desc)$")]
    public string Direction { get; set; } = "asc";
}
