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
                    u."zoneId"                              AS "ZoneId",
                    u.senha                                 AS "Senha",
                    u."createdAt"                           AS "CreatedAt",
                    u.provider                              AS "Provider",
                    u.provider_user_id                      AS "ProviderUserId",
                    u.addr_zip_code                         AS "AddrZipCode",
                    u.addr_street                           AS "AddrStreet",
                    u.addr_number                           AS "AddrNumber",
                    u.addr_neighborhood                     AS "AddrNeighborhood",
                    u.addr_city                             AS "AddrCity",
                    u.addr_state                            AS "AddrState",
                    u.addr_complement                       AS "AddrComplement",
                    u.addr_reference                        AS "AddrReference",
                    p.id                                    AS "ProfessionalId",
                    COALESCE(p."mpConnected", false)        AS "MpConnected"
                FROM "User" u
                LEFT JOIN "Professional" p ON p."userId" = u.id
                WHERE u.email = {0}
                """,
                email)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var valid = BCrypt.Net.BCrypt.Verify(password, row.Senha);
        if (!valid) return null;

        object? defaultAddress = null;
        if (!string.IsNullOrWhiteSpace(row.AddrZipCode))
        {
            defaultAddress = new
            {
                zipCode      = row.AddrZipCode,
                street       = row.AddrStreet,
                number       = row.AddrNumber,
                neighborhood = row.AddrNeighborhood,
                city         = row.AddrCity,
                state        = row.AddrState,
                complement   = row.AddrComplement,
                reference    = row.AddrReference,
            };
        }

        return new
        {
            id = row.Id,
            name = row.Name,
            email = row.Email,
            phone = row.Phone,
            role = row.Role,
            zoneId = row.ZoneId,
            createdAt = row.CreatedAt,
            provider = row.Provider,
            providerUserId = row.ProviderUserId,
            professionalId = row.ProfessionalId,
            mpConnected = row.MpConnected,
            defaultAddress,
        };
    }

    private sealed class AuthRowWithProfessional
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public string Role { get; init; } = string.Empty;
        public string? ZoneId { get; init; }
        public string Senha { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public string? Provider { get; init; }
        public string? ProviderUserId { get; init; }
        public string? AddrZipCode { get; init; }
        public string? AddrStreet { get; init; }
        public string? AddrNumber { get; init; }
        public string? AddrNeighborhood { get; init; }
        public string? AddrCity { get; init; }
        public string? AddrState { get; init; }
        public string? AddrComplement { get; init; }
        public string? AddrReference { get; init; }
        public string? ProfessionalId { get; init; }
        public bool MpConnected { get; init; }
    }
}
