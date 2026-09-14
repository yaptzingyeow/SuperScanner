namespace SuperScanner.Domain.Documents;

public enum PageState
{
    Importing,
    Processing,
    NeedsCrop,
    Ready,
    Failed
}
