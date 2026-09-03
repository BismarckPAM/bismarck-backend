namespace Identity.Service.Models;

public class Department
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<User> Users { get; set; } = new List<User>();
}
