namespace SuperScanner.Domain.Documents;

public enum DocumentStatus
{
    Draft,
    Uploading,
    Processing,
    Ready,
    Editing,
    Exporting,
    Completed,
    Failed
}
