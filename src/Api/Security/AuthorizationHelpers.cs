using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Api.Security;

/// <summary>
/// Single source of truth for "who can access this resource" checks used across endpoint files.
/// Extracted from the ownership pattern already proven in MpOAuthEndpoints and WalletEndpoints.
/// </summary>
public static class AuthorizationHelpers
{
    public static string? GetJwtUserId(HttpContext context) =>
        context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? context.User?.FindFirst("sub")?.Value
        ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static string? GetJwtRole(HttpContext context) =>
        context.User?.FindFirst("role")?.Value
        ?? context.User?.FindFirst(ClaimTypes.Role)?.Value;

    public static bool IsAdmin(HttpContext context) =>
        string.Equals(GetJwtRole(context), "admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if the authenticated user owns this professional profile or is an admin.
    ///
    /// Ownership check (in priority order):
    ///   1. role == "admin"                    → always allowed
    ///   2. professional.UserId == jwtUserId    → standard case (JWT sub = User.Id)
    ///   3. professional.Id == jwtUserId        → backwards-compat for legacy sessions
    ///                                             where sub was saved as professional.Id
    /// </summary>
    public static bool IsOwnerOrAdmin(HttpContext context, Professional professional)
    {
        if (IsAdmin(context))
            return true;

        var jwtUserId = GetJwtUserId(context);
        if (jwtUserId is null) return false;

        if (professional.UserId == jwtUserId) return true;
        if (professional.Id == jwtUserId) return true;

        return false;
    }

    /// <summary>Returns a 403/401 IResult if the caller is not an admin, otherwise null.</summary>
    public static IResult? RequireAdmin(HttpContext context)
    {
        var jwtUserId = GetJwtUserId(context);
        if (jwtUserId is null)
            return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

        if (!IsAdmin(context))
            return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

        return null;
    }

    /// <summary>Returns a 403/401 IResult unless the JWT subject equals routeId or the caller is an admin.</summary>
    public static IResult? RequireSelfOrAdmin(HttpContext context, string routeId)
    {
        var jwtUserId = GetJwtUserId(context);
        if (jwtUserId is null)
            return Results.Json(new { error = "Autenticação necessária" }, statusCode: 401);

        if (IsAdmin(context))
            return null;

        if (!string.Equals(jwtUserId, routeId, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "Acesso negado" }, statusCode: 403);

        return null;
    }

    /// <summary>
    /// Resolves the professional record owned by the authenticated user, unless an admin
    /// passes an explicit override. Same pattern already used by WalletEndpoints.
    /// </summary>
    public static async Task<(string? Id, IResult? Error)> ResolveProfessionalIdAsync(
        HttpContext context,
        string? adminOverride,
        AppDbContext ctx,
        CancellationToken ct)
    {
        var jwtUserId = GetJwtUserId(context);
        if (string.IsNullOrWhiteSpace(jwtUserId))
            return (null, Results.Json(new { error = "Autenticação obrigatória" }, statusCode: 401));

        if (IsAdmin(context) && !string.IsNullOrWhiteSpace(adminOverride))
            return (adminOverride, null);

        var professionalId = await ctx.Professionals
            .AsNoTracking()
            .Where(p => p.UserId == jwtUserId)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);

        if (professionalId is null)
            return (null, Results.Json(new { error = "Profissional não encontrado para este usuário" }, statusCode: 404));

        return (professionalId, null);
    }
}
