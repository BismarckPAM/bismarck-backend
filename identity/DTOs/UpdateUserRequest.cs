using System.Text.Json.Serialization;

namespace Identity.Service.DTOs;

public class UpdateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    [JsonRequired]
    public Guid RoleId { get; set; }

    [JsonRequired]
    public Guid DepartmentId { get; set; }
    public bool? IsActive { get; set; }
}
