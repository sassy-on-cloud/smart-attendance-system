using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartAttendance.API.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateAttendanceDto : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int StudentId { get; set; }

    [Range(1, int.MaxValue)]
    public int CourseId { get; set; }

    [Required]
    public DateTimeOffset? Date { get; set; }

    [Required, RegularExpression("^(Present|Absent|Late)$")]
    public string Status { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Date.HasValue && Date.Value.UtcDateTime == DateTime.MinValue)
            yield return new ValidationResult("Date must be a valid attendance timestamp.", [nameof(Date)]);
    }
}
