using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace HomeStack.Core;

public interface ICurrentUser
{
    int UserId { get; }
    int HouseholdId { get; }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? throw new InvalidOperationException("No HTTP context");

    public int UserId => int.Parse(Principal.FindFirstValue(HomeStackClaims.UserId)
        ?? throw new UnauthorizedAccessException());
    public int HouseholdId => int.Parse(Principal.FindFirstValue(HomeStackClaims.HouseholdId)
        ?? throw new UnauthorizedAccessException());
}

public static class HomeStackClaims
{
    public const string UserId = "hs:uid";
    public const string HouseholdId = "hs:hid";
}
