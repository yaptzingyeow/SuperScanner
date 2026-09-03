using Microsoft.AspNetCore.Authentication;
using SuperScanner.Api.Auth;
using SuperScanner.Application.Abstractions;
using SuperScanner.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.Configure<FirebaseAuthOptions>(
    builder.Configuration.GetSection(FirebaseAuthOptions.SectionName));
builder.Services.AddHttpClient<IFirebaseAppCheckTokenVerifier, FirebaseAppCheckTokenVerifier>(client =>
    client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<IFirebaseIdTokenVerifier, FirebaseAdminIdTokenVerifier>();
builder.Services.AddSingleton<IRequestIdentityVerifier, FirebaseRequestIdentityVerifier>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services
    .AddAuthentication(FirebaseAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, FirebaseAuthenticationHandler>(
        FirebaseAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<AppCheckMiddleware>();
app.UseAuthorization();

app.MapGet("/api/me", (ICurrentUser currentUser) =>
        Results.Ok(new { firebaseUid = currentUser.FirebaseUid }))
    .RequireAuthorization();

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
