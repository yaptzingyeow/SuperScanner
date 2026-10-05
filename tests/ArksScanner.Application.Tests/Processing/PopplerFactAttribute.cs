namespace ArksScanner.Application.Tests.Processing;

/// <summary>Runs only where Poppler's pdfinfo is installed (CI and the Worker image); skipped elsewhere.</summary>
public sealed class PopplerFactAttribute : FactAttribute
{
    public PopplerFactAttribute()
    {
        if (!OnPath(OperatingSystem.IsWindows() ? "pdfinfo.exe" : "pdfinfo"))
            Skip = "Poppler (pdfinfo) is not installed on this machine.";
    }

    private static bool OnPath(string file) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, file)));
}
