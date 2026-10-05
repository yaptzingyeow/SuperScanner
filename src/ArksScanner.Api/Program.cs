using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ArksScanner.Api.Auth;
using ArksScanner.Api.Endpoints;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Documents;
using ArksScanner.Application.Ocr;
using ArksScanner.Application.Uploads;
using ArksScanner.Infrastructure.Auth;
using ArksScanner.Infrastructure.Persistence;
using ArksScanner.Infrastructure.Processing;
using ArksScanner.Infrastructure.ObjectStorage;
using ArksScanner.Infrastructure.Auditing;
using ArksScanner.Infrastructure.Ocr;
using ArksScanner.Infrastructure.TextEditing;
using Microsoft.Extensions.Options;
using ArksScanner.Application.TextEditing;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsEnvironment("E2E"))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole();
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
}

var e2eIdentityConfigured = builder.Configuration[E2eIdentityFixture.ConfigurationKey] is not null;
if (!builder.Environment.IsEnvironment("E2E") && e2eIdentityConfigured)
{
    throw new InvalidOperationException(
        "E2E identity configuration is forbidden outside the E2E environment.");
}

var e2eIdentityEnabled = builder.Environment.IsEnvironment("E2E") &&
    builder.Configuration.GetValue<bool>(E2eIdentityFixture.ConfigurationKey);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.Configure<FirebaseAuthOptions>(
    builder.Configuration.GetSection(FirebaseAuthOptions.SectionName));
if (e2eIdentityEnabled)
{
    builder.Services.AddSingleton<IRequestIdentityVerifier, E2eRequestIdentityVerifier>();
}
else
{
    builder.Services.AddHttpClient<IFirebaseAppCheckTokenVerifier, FirebaseAppCheckTokenVerifier>(client =>
        client.Timeout = TimeSpan.FromSeconds(10));
    builder.Services.AddSingleton<FirebaseAdminIdTokenVerifier>();
    builder.Services.AddSingleton<IFirebaseIdTokenVerifier>(services => services.GetRequiredService<FirebaseAdminIdTokenVerifier>());
    builder.Services.AddSingleton<ArksScanner.Application.Plans.IIdentityAccountDeleter>(services =>
        services.GetRequiredService<FirebaseAdminIdTokenVerifier>());
    builder.Services.AddSingleton<IRequestIdentityVerifier, FirebaseRequestIdentityVerifier>();
}
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services
    .AddAuthentication(FirebaseAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, FirebaseAuthenticationHandler>(
        FirebaseAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization(AuthPolicies.AddSignedInAccount);
builder.Services.AddHealthChecks()
    .AddCheck<ArksScanner.Api.Health.DatabaseReadinessCheck>("database", tags: [ArksScanner.Api.Health.DatabaseReadinessCheck.Tag]);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSql") ?? string.Empty));
builder.Services.AddScoped<IDocumentRepository, EfDocumentRepository>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ArksScanner.Application.Plans.IAccountDirectory, EfAccountDirectory>();
builder.Services.AddSingleton<PlanSettingsCache>();
builder.Services.AddSingleton(PlanSeed.FromConfiguration(builder.Configuration));
builder.Services.AddScoped<ArksScanner.Application.Plans.IPlanRepository, EfPlanRepository>();
builder.Services.AddScoped<ArksScanner.Application.Plans.PlanService>();
builder.Services.AddScoped<ArksScanner.Infrastructure.Admin.AdminService>();
builder.Services.AddScoped<ArksScanner.Infrastructure.Privacy.PrivacyService>();
builder.Services.AddScoped<IDocumentExportRepository, EfDocumentExportRepository>();
builder.Services.AddScoped<IPageSignatureRepository, EfPageSignatureRepository>();
builder.Services.AddScoped<IPageMarkRepository, EfPageMarkRepository>();
builder.Services.AddSingleton<ArksScanner.Infrastructure.Signatures.SignatureImageNormalizer>();
builder.Services.AddSingleton(new DocumentExportPolicy(builder.Configuration.GetValue("DocumentExport:RetentionDays", 7)));
builder.Services.AddScoped<CreateDocumentExport>();
builder.Services.AddScoped<GetDocumentExportPreview>();
builder.Services.AddScoped<GetDocumentExport>();
builder.Services.AddSingleton<IClock, ArksScanner.Infrastructure.Time.SystemClock>();
builder.Services.AddScoped<CreateDocument>();
builder.Services.AddScoped<ListDocuments>();
builder.Services.AddScoped<DeleteDocument>();
builder.Services.AddScoped<RenameDocument>();
builder.Services.AddScoped<ReorderPages>();
builder.Services.AddScoped<RemovePage>();
builder.Services.AddScoped<IUploadIntentRepository, EfUploadIntentRepository>();
builder.Services.AddSingleton(new UploadPolicy(50, 25 * 1024 * 1024));
builder.Services.AddScoped<CreateUploadIntent>();
builder.Services.AddScoped<IProcessingJobQueue, PostgresJobQueue>();
builder.Services.AddScoped<IOcrRepository, EfOcrRepository>();
builder.Services.AddScoped<RequestPageOcr>();
builder.Services.AddScoped<GetPageOcr>();
builder.Services.AddOptions<OcrOptions>()
    .BindConfiguration(OcrOptions.SectionName)
    .Validate(options => options.IsValid(builder.Environment.EnvironmentName),
        "OCR configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<DocumentImportOptions>()
    .BindConfiguration(DocumentImportOptions.SectionName)
    .Validate(options => options.IsValid(), "Document import configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<PdfExportOptions>()
    .BindConfiguration(PdfExportOptions.SectionName)
    .Validate(options => options.IsValid(), "PDF export text-layer configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton(services => services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PdfExportOptions>>().Value);
builder.Services.AddOptions<TextEditingOptions>()
    .BindConfiguration(TextEditingOptions.SectionName)
    .Validate(options => options.IsValid(), "Text editing configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton<IFontCatalogue>(services =>
{
    var options = services.GetRequiredService<IOptions<TextEditingOptions>>().Value;
    return BundledFontCatalogue.Load(Path.Combine(AppContext.BaseDirectory, options.FontManifestPath));
});
builder.Services.AddSingleton<IPdfTextLayerWriter>(services => new PdfSharpTextLayerWriter(
    Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "NotoSans-Regular.ttf"),
    services.GetRequiredService<PdfExportOptions>().ToLimits()));
builder.Services.AddScoped<ITextSelectionRepository, EfTextEditRepository>();
builder.Services.AddScoped<ITextEditCommandRepository, EfTextEditRepository>();
builder.Services.AddScoped<ITextEditReadRepository, EfTextEditRepository>();
builder.Services.AddScoped<ITextRevisionSwitchRepository, EfTextEditRepository>();
builder.Services.AddScoped<SwitchPageRevision>();
builder.Services.AddSingleton<ITextGlyphPainter, MagickGlyphPainter>();
builder.Services.AddSingleton<ITextEditRenderer>(services => new TextEditRenderer(
    services.GetRequiredService<IFontCatalogue>(), AppContext.BaseDirectory,
    services.GetRequiredService<IOptions<TextEditingOptions>>().Value,
    services.GetRequiredService<ITextGlyphPainter>()));
builder.Services.AddScoped<TextEditPreview>(services =>
{
    var options = services.GetRequiredService<IOptions<TextEditingOptions>>().Value;
    return new TextEditPreview(services.GetRequiredService<ITextSelectionRepository>(),
        services.GetRequiredService<IObjectStore>(),
        services.GetRequiredService<ITextEditRenderer>(),
        new TextEditLimits(options.Enabled, options.MaxSelectionWords,
            options.MaxReplacementCharacters, options.MaxReplacementBoxArea,
            options.MaxQueuedEditsPerPage));
});
builder.Services.AddScoped<ITextEditPreparation>(services => new TextEditPreparation(
    services.GetRequiredService<IObjectStore>(),
    services.GetRequiredService<IFontCatalogue>(), AppContext.BaseDirectory,
    services.GetRequiredService<IOptions<TextEditingOptions>>().Value));
builder.Services.AddScoped<CreateTextEdit>(services =>
{
    var options = services.GetRequiredService<IOptions<TextEditingOptions>>().Value;
    return new CreateTextEdit(
        services.GetRequiredService<ITextEditCommandRepository>(),
        services.GetRequiredService<ITextEditPreparation>(),
        services.GetRequiredService<IProcessingJobQueue>(),
        services.GetRequiredService<IAuditWriter>(),
        services.GetRequiredService<IClock>(),
        new TextEditLimits(options.Enabled, options.MaxSelectionWords,
            options.MaxReplacementCharacters, options.MaxReplacementBoxArea,
            options.MaxQueuedEditsPerPage));
});
builder.Services.AddScoped<GetTextEdit>();
builder.Services.AddScoped<GetPageEditHistory>();
builder.Services.AddScoped<ITextStyleEstimator>(services => new TextStyleEstimator(
    services.GetRequiredService<IObjectStore>(),
    services.GetRequiredService<IFontCatalogue>(),
    AppContext.BaseDirectory));
builder.Services.AddScoped<ProposeTextStyle>(services => new ProposeTextStyle(
    services.GetRequiredService<ITextSelectionRepository>(),
    services.GetRequiredService<ITextStyleEstimator>(),
    services.GetRequiredService<IOptions<TextEditingOptions>>().Value.MaxSelectionWords));
builder.Services.AddScoped<CompleteUpload>();
builder.Services.AddScoped<GetUploadStatus>();
builder.Services.Configure<R2Options>(builder.Configuration.GetSection(R2Options.SectionName));
builder.Services.AddSingleton<IObjectStore, R2ObjectStore>();
builder.Services.Configure<AuditOptions>(builder.Configuration.GetSection(AuditOptions.SectionName));
builder.Services.AddScoped<IAuditWriter, HmacAuditWriter>();
builder.Services.AddScoped<IAuditVerifier, HmacAuditVerifier>();

var app = builder.Build();
_ = app.Services.GetRequiredService<IFontCatalogue>();

if (builder.Environment.IsEnvironment("E2E"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await database.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsEnvironment("E2E"))
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseMiddleware<AppCheckMiddleware>();
app.UseAuthorization();
app.UseMiddleware<AccountTrackingMiddleware>();

app.MapGet("/api/me", (ICurrentUser currentUser) =>
        Results.Ok(new { firebaseUid = currentUser.FirebaseUid }))
    .RequireAuthorization();
DocumentsEndpoints.Map(app);
PageManagementEndpoints.Map(app);
DocumentExportEndpoints.Map(app);
DocumentPreviewEndpoints.Map(app);
PageSignatureEndpoints.Map(app);
PageMarkEndpoints.Map(app);
CropEndpoints.Map(app);
PageRepairEndpoints.Map(app);
OcrEndpoints.Map(app);
TextEditingEndpoints.Map(app);
AdminEndpoints.Map(app);
PlanEndpoints.Map(app);
UploadsEndpoints.Map(app);
// /health: the process is up. /health/ready: database reachable and fully migrated.
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(ArksScanner.Api.Health.DatabaseReadinessCheck.Tag),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Description),
        });
    },
});
if (e2eIdentityEnabled)
{
    E2eIdentityFixture.Map(app);
}

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
