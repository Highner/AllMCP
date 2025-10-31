using System;
using System.Collections.Generic;

namespace AllMCPSolution.Models;

public sealed record SisterhoodConnectionSharedSisterhood(Guid SisterhoodId, string Name, string? Description);

public sealed record SisterhoodConnectionUser(
    Guid UserId,
    string DisplayName,
    string AvatarLetter,
    string? Email,
    IReadOnlyList<SisterhoodConnectionSharedSisterhood> SharedSisterhoods)
{
    public int SharedSisterhoodCount => SharedSisterhoods.Count;
}
