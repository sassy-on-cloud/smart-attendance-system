using Microsoft.OpenApi;
using SmartAttendance.API.Controllers;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SmartAttendance.API.Recognition;

public sealed class RecognitionOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(RecognitionController)) return;
        var enrollment = context.MethodInfo.Name == nameof(RecognitionController.Enroll);
        operation.Summary = enrollment ? "Enrollment contract preview (Admin only)" : "Session recognition contract preview (assigned staff only)";
        operation.Description = "Integration is disabled: the default adapter returns 503 ServiceUnavailable. " +
            "Automated tests inject an explicit mock; 200 responses are labeled simulations. " +
            "No biometric enrollment is stored and attendance is never recorded. " +
            "Only captureId is accepted in the body; identity, method, confidence and success claims cannot be supplied. " +
            "Recognition requires a nonlegacy Open session with StartsAtUtc <= now < EndsAtUtc. " +
            "UnknownFace/NoFace/MultipleFaces/AmbiguousMatch return 422; duplicate attendance returns 409. " +
            "Invalid component bindings return 502, unavailability 503 and timeout 504. " +
            "See docs/RECOGNITION_INTEGRATION.md for the trusted local component contract and approval gates.";
    }
}
