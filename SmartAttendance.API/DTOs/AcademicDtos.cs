using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartAttendance.API.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class EnrollStudentDto
{
    [Range(1, int.MaxValue)]
    public int StudentId { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class AssignTeacherDto
{
    [Range(1, int.MaxValue)]
    public int TeacherUserId { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateAttendanceSessionDto : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int CourseId { get; set; }
    [Required]
    public DateTimeOffset? StartsAt { get; set; }
    [Required]
    public DateTimeOffset? EndsAt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartsAt.HasValue && EndsAt.HasValue &&
            (StartsAt.Value.UtcDateTime == DateTime.MinValue || EndsAt <= StartsAt))
            yield return new ValidationResult("EndsAt must follow a valid StartsAt.", [nameof(StartsAt), nameof(EndsAt)]);
    }
}

public class AttendanceSessionResponseDto
{
    public int AttendanceSessionId { get; set; }
    public int CourseId { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public bool IsLegacy { get; set; }
}

public class EnrollmentResponseDto
{
    public int StudentId { get; set; }
    public int CourseId { get; set; }
    public bool IsLegacyInferred { get; set; }
}

public class TeacherAssignmentResponseDto
{
    public int TeacherUserId { get; set; }
    public int CourseId { get; set; }
}
