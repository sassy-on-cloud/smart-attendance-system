using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartAttendance.API.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateStudentDto
{
    [Range(1, int.MaxValue)]
    public int UserId { get; set; }

    [Required, StringLength(50)]
    [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9._/\-]*$")]
    public string RollNumber { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Department { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Semester { get; set; }
}
