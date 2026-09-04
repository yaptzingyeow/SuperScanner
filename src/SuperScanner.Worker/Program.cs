using Microsoft.EntityFrameworkCore;
using SuperScanner.Worker;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Uploads;
using SuperScanner.Infrastructure.ObjectStorage;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;
using SuperScanner.Infrastructure.Security;
using SuperScanner.Infrastructure.Auditing;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsEnvironment("E2E"))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole();
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSql") ?? string.Empty));
builder.Services.AddSingleton<IClock, SuperScanner.Infrastructure.Time.SystemClock>();
builder.Services.AddScoped<IProcessingJobQueue, PostgresJobQueue>();
builder.Services.AddScoped<IUploadValidationRepository, EfUploadValidationRepository>();
builder.Services.AddSingleton(new UploadValidationPolicy(25 * 1024 * 1024));
builder.Services.AddScoped<ValidateUpload>();
builder.Services.Configure<R2Options>(builder.Configuration.GetSection(R2Options.SectionName));
builder.Services.AddSingleton<IObjectStore, R2ObjectStore>();
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.SectionName));
builder.Services.AddSingleton<IMalwareScanner, ClamAvMalwareScanner>();
builder.Services.Configure<AuditOptions>(builder.Configuration.GetSection(AuditOptions.SectionName));
builder.Services.AddScoped<IAuditWriter, HmacAuditWriter>();
builder.Services.AddSingleton<UploadValidationJobRunner>();
builder.Services.AddHostedService<UploadValidationWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();

public partial class Program;
