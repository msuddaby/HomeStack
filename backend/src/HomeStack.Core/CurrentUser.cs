using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace HomeStack.Core;

public interface ICurrentUser
{
    int UserId { get; }
    int HouseholdId { get; }
}

/// <summary>
/// Lets background jobs act on behalf of a household. Call <see cref="Enter"/> on a fresh DI scope
/// before resolving module services; <see cref="ICurrentUser"/> then reports that household (and user id 0).
/// </summary>
public sealed class HouseholdScope
{
    public int? HouseholdId { get; private set; }
    public void Enter(int householdId) => HouseholdId = householdId;
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, HouseholdScope scope) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? throw new InvalidOperationException("No HTTP context");

    public int UserId => scope.HouseholdId is not null ? 0 : int.Parse(Principal.FindFirstValue(HomeStackClaims.UserId)
        ?? throw new UnauthorizedAccessException());
    public int HouseholdId => scope.HouseholdId ?? int.Parse(Principal.FindFirstValue(HomeStackClaims.HouseholdId)
        ?? throw new UnauthorizedAccessException());
}

public static class HomeStackClaims
{
    public const string UserId = "hs:uid";
    public const string HouseholdId = "hs:hid";
}
