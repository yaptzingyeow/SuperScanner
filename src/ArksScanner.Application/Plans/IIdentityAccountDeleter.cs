namespace ArksScanner.Application.Plans;

/// <summary>Deletes the person's sign-in (Firebase user) so a deleted account cannot sign back in to old data.</summary>
public interface IIdentityAccountDeleter
{
    Task DeleteAsync(string uid, CancellationToken ct);
}
