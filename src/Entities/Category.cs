namespace Recruitment.Api.Entities;

public class Category
{
    public string CategoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
