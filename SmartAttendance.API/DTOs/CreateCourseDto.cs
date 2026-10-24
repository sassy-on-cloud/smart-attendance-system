using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartAttendance.API.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateCourseDto
{
    [Required, StringLength(200)]
    public string CourseName { get; set; } = string.Empty;

    [Required, StringLength(30)]
    [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9._/\-]*$")]
    public string CourseCode { get; set; } = string.Empty;
}
