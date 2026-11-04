using System.ComponentModel.DataAnnotations;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Dtos;

public class CreateJobRequest
{
    [Required(ErrorMessage = "Title không được để trống")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description không được để trống")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Requirements không được để trống")]
    public string Requirements { get; set; } = string.Empty;

    [Required(ErrorMessage = "Location không được để trống")]
    public string Location { get; set; } = string.Empty;

    [Required(ErrorMessage = "JobType không được để trống")]
    public JobType JobType { get; set; }

    [Required(ErrorMessage = "Experience không được để trống")]
    public string Experience { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Số lượng tuyển dụng phải lớn hơn hoặc bằng 1")]
    public int NumberOfPositions { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Lương tối thiểu không được âm")]
    public decimal? SalaryMin { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Lương tối đa không được âm")]
    public decimal? SalaryMax { get; set; }

    [Required(ErrorMessage = "Deadline không được để trống")]
    public DateTime Deadline { get; set; }

    [Required(ErrorMessage = "CategoryId không được để trống")]
    public string CategoryId { get; set; } = string.Empty;

    [MinLength(1, ErrorMessage = "Phải chọn ít nhất 1 kỹ năng")]
    public List<string> SkillIds { get; set; } = new();
}
