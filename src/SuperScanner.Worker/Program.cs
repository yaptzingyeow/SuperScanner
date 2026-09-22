using Microsoft.EntityFrameworkCore;
using SuperScanner.Worker;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Uploads;
using SuperScanner.Infrastructure.ObjectStorage;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;
using SuperScanner.Infrastructure.Security;
using SuperScanner.Infrastructure.Auditing;
using SuperScanner.Infrastructure.Ocr;
using SuperScanner.Application.Ocr;

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
builder.Services.AddScoped<IOcrRepository, EfOcrRepository>();
builder.Services.AddOptions<OcrOptions>()
    .BindConfiguration(OcrOptions.SectionName)
    .Validate(options => options.IsValid(builder.Environment.EnvironmentName),
        "OCR configuration is invalid.")
    .ValidateOnStart();
if (string.Equals(builder.Configuration["Ocr:Provider"], "Fake", StringComparison.Ordinal))
    builder.Services.AddSingleton<IOcrProvider, FakeOcrProvider>();
builder.Services.AddSingleton<OcrMetrics>();
builder.Services.AddScoped<OcrProcessor>();
builder.Services.AddScoped<OcrJobScheduler>();
builder.Services.AddScoped<IUploadValidationRepository, EfUploadValidationRepository>();
builder.Services.AddSingleton(new UploadValidationPolicy(25 * 1024 * 1024));
builder.Services.AddScoped<ValidateUpload>();
builder.Services.Configure<R2Options>(builder.Configuration.GetSection(R2Options.SectionName));
builder.Services.AddSingleton<R2ObjectStore>();
builder.Services.AddSingleton<IObjectStore>(sp => sp.GetRequiredService<R2ObjectStore>());
builder.Services.AddScoped<DocumentPreviewProcessor>();
builder.Services.AddOptions<DocumentImportOptions>()
    .BindConfiguration(DocumentImportOptions.SectionName)
    .Validate(options => options.IsValid(), "Document import configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddScoped<IPdfImportTool, PopplerPdfImportTool>();
builder.Services.AddScoped<DocumentImportProcessor>();
builder.Services.AddSingleton(new DocumentPdfLimits
{
    MaxPdfBytes = builder.Configuration.GetValue("DocumentExport:MaxOutputBytes", 104_857_600L)
});
builder.Services.AddScoped<DocumentPdfBuilder>();
builder.Services.AddOptions<DocumentBoundaryOptions>()
    .BindConfiguration(DocumentBoundaryOptions.SectionName)
    .Validate(options => options.IsValid(), "Document boundary configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton<DocumentBoundaryHealth>();
builder.Services.AddScoped<CropProcessor>();
ImageMagick.ResourceLimits.Memory = 256UL * 1024 * 1024;
ImageMagick.ResourceLimits.Disk = 1024UL * 1024 * 1024;
ImageMagick.ResourceLimits.Width = 20000;
ImageMagick.ResourceLimits.Height = 20000;
ImageMagick.ResourceLimits.Thread = 2;
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.SectionName));
var malwareScanningEnabled = builder.Configuration.GetValue("MalwareScanning:Enabled", true);
if (malwareScanningEnabled)
{
    builder.Services.AddSingleton<IMalwareScanner, ClamAvMalwareScanner>();
}
else
{
    builder.Services.AddSingleton<IMalwareScanner, DisabledMalwareScanner>();
}
builder.Services.Configure<AuditOptions>(builder.Configuration.GetSection(AuditOptions.SectionName));
builder.Services.AddScoped<IAuditWriter, HmacAuditWriter>();
builder.Services.AddSingleton<UploadValidationJobRunner>();
builder.Services.AddHostedService<UploadValidationWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();

public partial class Program;
