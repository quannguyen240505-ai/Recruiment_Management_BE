namespace Recruitment.Api.Entities;

public class JobSkill
{
    public string JobId { get; set; } = string.Empty;
    public Job Job { get; set; } = null!;

    public string SkillId { get; set; } = string.Empty;
    public Skill Skill { get; set; } = null!;
}
