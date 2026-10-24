using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartAttendance.API.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CorrectAttendanceDto
{
    [Required, RegularExpression("^(Present|Absent|Late|Excused)$")]
    public string Status { get; set; } = string.Empty;
    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class UpdateAttendanceSessionDto : IValidatableObject
{
    [Required]
    public DateTimeOffset? StartsAt { get; set; }
    [Required]
    public DateTimeOffset? EndsAt { get; set; }
    [Required, RegularExpression("^(Open|Closed|Cancelled)$")]
    public string State { get; set; } = string.Empty;
    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;
    [Range(1, int.MaxValue)]
    public int ExpectedVersion { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartsAt.HasValue && EndsAt.HasValue &&
            (StartsAt.Value.UtcDateTime == DateTime.MinValue || EndsAt <= StartsAt))
            yield return new ValidationResult("EndsAt must follow a valid StartsAt.", [nameof(StartsAt), nameof(EndsAt)]);
    }
}

public class AttendanceHistoryResponseDto
{
    public long AttendanceHistoryId { get; set; }
    public int ActorUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string? PreviousStatus { get; set; }
    public string? PreviousMethod { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Version { get; set; }
}

public class AttendanceSessionHistoryResponseDto
{
    public long AttendanceSessionHistoryId { get; set; }
    public int ActorUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string? PreviousState { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? PreviousStartsAtUtc { get; set; }
    public DateTime? PreviousEndsAtUtc { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Version { get; set; }
}
