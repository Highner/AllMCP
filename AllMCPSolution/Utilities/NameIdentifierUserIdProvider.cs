using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace AllMCPSolution.Utilities;

public class NameIdentifierUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}