using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Recognition;
using SmartAttendance.API.Security;

namespace SmartAttendance.API.Controllers;

[ApiController]
[Route("api/recognition")]
[Authorize(Policy = AuthPolicies.CurrentStaff)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(2048)]
[Tags("Recognition integration contract (disabled; simulation only in tests)")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
[ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
[ProducesResponseType(StatusCodes.Status502BadGateway)]
[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
[ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
public sealed class RecognitionController(RecognitionWorkflow workflow) : ControllerBase
{
    /// <summary>Enrollment contract preview. No biometric enrollment is stored in this milestone.</summary>
    [HttpPost("students/{studentId}/enrollment")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(typeof(RecognitionEnrollmentResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecognitionEnrollmentResponseDto>> Enroll([Range(1, int.MaxValue)] int studentId,
        RecognitionEnrollmentRequestDto request, CancellationToken ct)
    {
        var actor = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await workflow.EnrollAsync(studentId, request.CaptureId, actor, ct);
        return StatusCode(result.HttpStatus, new RecognitionEnrollmentResponseDto(result.RequestId, result.Outcome, result.StudentId, result.IsSimulation));
    }

    /// <summary>Session recognition contract preview. Attendance is never recorded by this endpoint.</summary>
    [HttpPost("sessions/{sessionId}/recognize")]
    [ProducesResponseType(typeof(RecognitionResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecognitionResponseDto>> Recognize([Range(1, int.MaxValue)] int sessionId,
        RecognitionRequestDto request, CancellationToken ct)
    {
        var actor = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await workflow.RecognizeAsync(sessionId, request.CaptureId, User.IsInRole(AppRoles.Admin), actor, ct);
        return StatusCode(result.HttpStatus, new RecognitionResponseDto(result.RequestId, sessionId, result.Outcome,
            result.StudentId, result.Confidence, result.IsSimulation));
    }
}
