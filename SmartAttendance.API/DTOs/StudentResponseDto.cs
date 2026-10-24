
namespace SmartAttendance.API.DTOs
{
    public class StudentResponseDto
    {
        public int StudentId { get; set; }
        public int UserId { get; set; }
        public string RollNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public int Semester { get; set; }
    }
}
