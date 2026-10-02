namespace ArksScanner.Domain.Documents;

public enum DocumentExportState
{
    Queued,
    Processing,
    Ready,
    Failed
}
