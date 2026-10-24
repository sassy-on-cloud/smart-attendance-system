using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartAttendance.API.DTOs;

public sealed class NonEmptyGuidAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is Guid id && id != Guid.Empty;
}

// The capture handle is untrusted input. Only the server-side adapter may resolve it.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecognitionEnrollmentRequestDto
{
    [Required, NonEmptyGuid] public Guid CaptureId { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecognitionRequestDto
{
    [Required, NonEmptyGuid] public Guid CaptureId { get; set; }
}

public sealed record RecognitionEnrollmentResponseDto(Guid RequestId, string Outcome, int? StudentId,
    bool IsSimulation, bool EnrollmentStored = false);

public sealed record RecognitionResponseDto(Guid RequestId, int AttendanceSessionId, string Outcome,
    int? StudentId, double? Confidence, bool IsSimulation, bool AttendanceRecorded = false);
