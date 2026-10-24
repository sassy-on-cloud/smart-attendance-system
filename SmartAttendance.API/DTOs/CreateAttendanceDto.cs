using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartAttendance.API.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateAttendanceDto
{
    [Range(1, int.MaxValue)]
    public int StudentId { get; set; }

    [Range(1, int.MaxValue)]
    public int AttendanceSessionId { get; set; }

    [Required, RegularExpression("^(Present|Absent|Late|Excused)$")]
    public string Status { get; set; } = string.Empty;
}
