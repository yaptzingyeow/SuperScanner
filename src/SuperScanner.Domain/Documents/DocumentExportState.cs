namespace SuperScanner.Domain.Documents;

public enum DocumentExportState
{
    Queued,
    Processing,
    Ready,
    Failed
}
