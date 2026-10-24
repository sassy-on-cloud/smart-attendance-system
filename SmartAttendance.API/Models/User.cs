namespace SmartAttendance.API.Models
{
    public class User
    {
        public int UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public string? NormalizedEmail { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;
    }
}
