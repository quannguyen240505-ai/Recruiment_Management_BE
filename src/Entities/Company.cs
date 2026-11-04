namespace Recruitment.Api.Entities;

public class Company
{
    public string CompanyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }

    public ICollection<User> Recruiters { get; set; } = new List<User>();
    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
