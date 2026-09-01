namespace SmartAttendance.API.Models
{
    public class Student
    {
        public int StudentId { get; set; }

        public string RollNumber { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;

        public int Semester { get; set; }

        public int UserId { get; set; }

        public User? User { get; set; }
    }
}