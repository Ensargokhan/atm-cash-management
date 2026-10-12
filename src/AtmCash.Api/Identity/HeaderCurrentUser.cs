using AtmCash.Application.Abstractions.Services;

namespace AtmCash.Api.Identity;

/// <summary>
/// Reads the current user from the X-User request header.
/// Authentication is out of scope for this assignment (see README); switching to a token means replacing only this class.
/// </summary>
public sealed class HeaderCurrentUser : ICurrentUser
{
    public const string HeaderName = "X-User";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HeaderCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName].ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
