using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Microsoft.AspNetCore.Identity;
using Recruitment.Api.Entities;

var builder = WebApplication.CreateBuilder(args);

// ---- Services (DI) ----
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // Enum trả về dạng OPEN, FULL_TIME... (khớp API Specification)
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

// --- CẤU HÌNH DATABASE & BẢO MẬT (SCRUM-128) ---
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
// -----------------------------------------------
// TODO SCRUM-129: AddAuthentication (JWT), AddCors, API conventions
// TODO: đăng ký các service nghiệp vụ tại đây, ví dụ:
// builder.Services.AddScoped<IJobService, JobService>();

var app = builder.Build();

// ---- Middleware pipeline ----
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
// TODO SCRUM-129: app.UseCors(...); app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");   // dùng cho kiểm tra hạ tầng / CI

app.Run();
