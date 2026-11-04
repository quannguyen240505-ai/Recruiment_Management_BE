namespace Recruitment.Api.Entities;

public class Skill
{
    public string SkillId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public ICollection<JobSkill> JobSkills { get; set; } = new List<JobSkill>();
}
