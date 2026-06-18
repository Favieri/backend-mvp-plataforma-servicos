using Application.Abstractions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AuthRepository(AppDbContext ctx) : IAuthRepository
{
    public async Task<object?> LoginAsync(string email, string password, CancellationToken ct)
    {
        // senha is not part of the User domain entity; project it via raw SQL to avoid exposing it elsewhere
        var row = await ctx.Database
            .SqlQueryRaw<AuthRowWithProfessional>(
                """
                SELECT
                    u.id                                    AS "Id",
                    u.name                                  AS "Name",
                    u.email                                 AS "Email",
                    u.phone                                 AS "Phone",
                    u.role                                  AS "Role",
                    u.senha                                 AS "Senha",
                    u."createdAt"                           AS "CreatedAt",
                    u.provider                              AS "Provider",
                    u.provider_user_id                      AS "ProviderUserId",
                    p.id                                    AS "ProfessionalId",
                    COALESCE(p.mp_connected, false)         AS "MpConnected"
                FROM "User" u
                LEFT JOIN professionals p ON p.user_id = u.id
                WHERE u.email = {0}
                """,
                email)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var valid = BCrypt.Net.BCrypt.Verify(password, row.Senha);
        if (!valid) return null;

        return new
        {
            id = row.Id,
            name = row.Name,
            email = row.Email,
            phone = row.Phone,
            role = row.Role,
            createdAt = row.CreatedAt,
            provider = row.Provider,
            providerUserId = row.ProviderUserId,
            professionalId = row.ProfessionalId,
            mpConnected = row.MpConnected,
        };
    }

    private sealed class AuthRowWithProfessional
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public string Role { get; init; } = string.Empty;
        public string Senha { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public string? Provider { get; init; }
        public string? ProviderUserId { get; init; }
        public string? ProfessionalId { get; init; }
        public bool MpConnected { get; init; }
    }
}
