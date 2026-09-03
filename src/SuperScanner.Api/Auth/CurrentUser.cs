using System.Security.Claims;

namespace SuperScanner.Api.Auth;

public interface ICurrentUser
{
    string FirebaseUid { get; }
}

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public string FirebaseUid =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No verified user is available for this request.");
}
