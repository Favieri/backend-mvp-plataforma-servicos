using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class UserRepository(AppDbContext ctx) : IUserRepository
{
    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct)
        => await ctx.Users.AsNoTracking().AnyAsync(u => u.Email == email, ct);

    public async Task<bool> ZoneExistsAndActiveAsync(string zoneId, CancellationToken ct)
        => await ctx.Zones.AsNoTracking().AnyAsync(z => z.Id == zoneId && z.Active, ct);

    public async Task<object> CreateAsync(string name, string email, string? phone, string role, string hashedPassword, string? zoneId, CancellationToken ct, AddressDto? defaultAddress = null)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        string? addrZip = null, addrStreet = null, addrNumber = null, addrNeighborhood = null;
        string? addrCity = null, addrState = null, addrComplement = null, addrReference = null;

        if (defaultAddress is not null)
        {
            addrZip = Application.Services.AddressResolver.NormalizeZipCode(defaultAddress.ZipCode);
            addrStreet = defaultAddress.Street.Trim();
            addrNumber = defaultAddress.Number.Trim();
            addrNeighborhood = defaultAddress.Neighborhood.Trim();
            addrCity = defaultAddress.City.Trim();
            addrState = defaultAddress.State.Trim();
            addrComplement = defaultAddress.Complement?.Trim();
            addrReference = defaultAddress.Reference?.Trim();
        }

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "User"(id, name, email, phone, role, senha, "zoneId", "createdAt",
                               addr_zip_code, addr_street, addr_number, addr_neighborhood,
                               addr_city, addr_state, addr_complement, addr_reference)
            VALUES ({id}, {name}, {email}, {phone}, {role}, {hashedPassword}, {zoneId}, {now},
                    {addrZip}, {addrStreet}, {addrNumber}, {addrNeighborhood},
                    {addrCity}, {addrState}, {addrComplement}, {addrReference})
            """, ct);

        return new
        {
            id, name, email, phone, role, zoneId, createdAt = now,
            defaultAddress = defaultAddress is not null
                ? new { zipCode = addrZip, street = addrStreet, number = addrNumber,
                        neighborhood = addrNeighborhood, city = addrCity, state = addrState,
                        complement = addrComplement, reference = addrReference }
                : null
        };
    }

    public async Task<AddressDto?> GetDefaultAddressAsync(string userId, CancellationToken ct)
    {
        var row = await ctx.Database
            .SqlQuery<AddressRow>($"""
                SELECT addr_zip_code AS "ZipCode", addr_street AS "Street", addr_number AS "Number",
                       addr_neighborhood AS "Neighborhood", addr_city AS "City", addr_state AS "State",
                       addr_complement AS "Complement", addr_reference AS "Reference"
                FROM "User"
                WHERE id = {userId}
                LIMIT 1
            """)
            .FirstOrDefaultAsync(ct);

        if (row is null || string.IsNullOrWhiteSpace(row.ZipCode))
            return null;

        return new AddressDto(row.ZipCode, row.Street!, row.Number!,
                              row.Neighborhood!, row.City!, row.State!,
                              row.Complement, row.Reference);
    }

    public async Task UpdateDefaultAddressAsync(string userId, AddressDto address, CancellationToken ct)
    {
        var zip = Application.Services.AddressResolver.NormalizeZipCode(address.ZipCode);
        var street = address.Street.Trim();
        var number = address.Number.Trim();
        var neighborhood = address.Neighborhood.Trim();
        var city = address.City.Trim();
        var state = address.State.Trim();
        var complement = address.Complement?.Trim();
        var reference = address.Reference?.Trim();

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "User"
            SET addr_zip_code = {zip},
                addr_street = {street},
                addr_number = {number},
                addr_neighborhood = {neighborhood},
                addr_city = {city},
                addr_state = {state},
                addr_complement = {complement},
                addr_reference = {reference}
            WHERE id = {userId}
            """, ct);
    }

    public async Task<object?> GetByIdAsync(string userId, CancellationToken ct)
    {
        var row = await ctx.Database
            .SqlQuery<SocialUserRow>($"""
                SELECT id AS "Id", name AS "Name", email AS "Email", phone AS "Phone",
                       role AS "Role", "zoneId" AS "ZoneId", "createdAt" AS "CreatedAt",
                       provider AS "Provider", provider_user_id AS "ProviderUserId"
                FROM "User"
                WHERE id = {userId}
                LIMIT 1
            """)
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var address = await GetDefaultAddressAsync(userId, ct);
        return ToUserObject(row, address);
    }

    public async Task UpdateUserAsync(string userId, string? name, string? phone, string? zoneId, CancellationToken ct)
    {
        if (name is not null)
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "User" SET name = {name} WHERE id = {userId}""", ct);

        if (phone is not null)
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "User" SET phone = {phone} WHERE id = {userId}""", ct);

        if (zoneId is not null)
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "User" SET "zoneId" = {zoneId} WHERE id = {userId}""", ct);
    }

    public async Task<(object User, bool IsNewUser)> FindOrCreateSocialUserAsync(string provider, string providerUserId, string email, string name, CancellationToken ct)
    {
        // 1. Search by provider + providerUserId
        var existing = await ctx.Database
            .SqlQuery<SocialUserRow>($"""
                SELECT id AS "Id", name AS "Name", email AS "Email", phone AS "Phone",
                       role AS "Role", "zoneId" AS "ZoneId", "createdAt" AS "CreatedAt",
                       provider AS "Provider", provider_user_id AS "ProviderUserId"
                FROM "User"
                WHERE provider = {provider} AND provider_user_id = {providerUserId}
                LIMIT 1
            """)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            var address = await GetDefaultAddressAsync(existing.Id, ct);
            return (ToUserObject(existing, address), false);
        }

        // 2. Search by email
        var byEmail = await ctx.Database
            .SqlQuery<SocialUserRow>($"""
                SELECT id AS "Id", name AS "Name", email AS "Email", phone AS "Phone",
                       role AS "Role", "zoneId" AS "ZoneId", "createdAt" AS "CreatedAt",
                       provider AS "Provider", provider_user_id AS "ProviderUserId"
                FROM "User"
                WHERE email = {email}
                LIMIT 1
            """)
            .FirstOrDefaultAsync(ct);

        if (byEmail is not null)
        {
            // Link provider to existing user if not already linked
            if (string.IsNullOrEmpty(byEmail.Provider))
            {
                await ctx.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE "User"
                    SET provider = {provider}, provider_user_id = {providerUserId}
                    WHERE id = {byEmail.Id}
                    """, ct);
                byEmail = byEmail with { Provider = provider, ProviderUserId = providerUserId };
            }
            var addressByEmail = await GetDefaultAddressAsync(byEmail.Id, ct);
            return (ToUserObject(byEmail, addressByEmail), false);
        }

        // 3. Create new user
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "User"(id, name, email, phone, role, senha, "zoneId", "createdAt", provider, provider_user_id)
            VALUES ({id}, {name}, {email}, {(string?)null}, {"cliente"}, {(string?)null}, {(string?)null}, {now}, {provider}, {providerUserId})
            """, ct);

        return (new
        {
            id, name, email, phone = (string?)null, role = "cliente",
            zoneId = (string?)null, createdAt = now,
            provider, providerUserId,
            defaultAddress = (object?)null
        }, true);
    }

    // ─── Confirmação de conta e recuperação de senha ──────────────────────────

    public async Task<(string Id, string Name, string Email, bool HasPassword, string? Provider)?> GetAuthInfoByEmailAsync(string email, CancellationToken ct)
    {
        var row = await ctx.Database
            .SqlQuery<AuthInfoRow>($"""
                SELECT id AS "Id", name AS "Name", email AS "Email",
                       (senha IS NOT NULL) AS "HasPassword", provider AS "Provider"
                FROM "User"
                WHERE email = {email}
                LIMIT 1
            """)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : (row.Id, row.Name, row.Email, row.HasPassword, row.Provider);
    }

    public async Task SetPasswordAsync(string userId, string hashedPassword, CancellationToken ct)
    {
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "User" SET senha = {hashedPassword} WHERE id = {userId}""", ct);
    }

    public async Task<(string Id, string Name, string Email, bool EmailVerified)?> GetVerificationInfoByEmailAsync(string email, CancellationToken ct)
    {
        var row = await ctx.Database
            .SqlQuery<VerificationInfoRow>($"""
                SELECT id AS "Id", name AS "Name", email AS "Email", email_verified AS "EmailVerified"
                FROM "User"
                WHERE email = {email}
                LIMIT 1
            """)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : (row.Id, row.Name, row.Email, row.EmailVerified);
    }

    public async Task SetEmailVerifiedAsync(string userId, CancellationToken ct)
    {
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "User" SET email_verified = true WHERE id = {userId}""", ct);
    }

    // ─── Múltiplos endereços (PRD-18a) ────────────────────────────────────────

    public async Task<IReadOnlyList<UserAddressDto>> GetAddressesAsync(string userId, CancellationToken ct)
    {
        var rows = await ctx.Database
            .SqlQuery<UserAddressRow>($"""
                SELECT id AS "Id", label AS "Label",
                       zip_code AS "ZipCode", street AS "Street",
                       number AS "Number", neighborhood AS "Neighborhood",
                       city AS "City", state AS "State",
                       complement AS "Complement", reference AS "Reference",
                       is_default AS "IsDefault", last_used_at AS "LastUsedAt"
                FROM user_address
                WHERE user_id = {userId}
                ORDER BY is_default DESC, last_used_at DESC NULLS LAST, created_at ASC
            """)
            .ToListAsync(ct);

        return rows.Select(r => new UserAddressDto(
            r.Id, r.Label, r.ZipCode, r.Street, r.Number,
            r.Neighborhood, r.City, r.State, r.Complement,
            r.Reference, r.IsDefault, r.LastUsedAt
        )).ToList();
    }

    public async Task<UserAddressDto> CreateAddressAsync(string userId, CreateUserAddressRequest req, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString();
        var zip = Application.Services.AddressResolver.NormalizeZipCode(req.ZipCode);

        if (req.SetAsDefault)
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE user_address SET is_default = false WHERE user_id = {userId}""", ct);

        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_address
              (id, user_id, label, zip_code, street, number, neighborhood,
               city, state, complement, reference, is_default, created_at)
            VALUES
              ({id}, {userId}, {req.Label}, {zip}, {req.Street},
               {req.Number}, {req.Neighborhood}, {req.City}, {req.State},
               {req.Complement}, {req.Reference}, {req.SetAsDefault}, now())
        """, ct);

        return new UserAddressDto(id, req.Label, zip, req.Street, req.Number,
            req.Neighborhood, req.City, req.State, req.Complement,
            req.Reference, req.SetAsDefault, null);
    }

    public async Task<UserAddressDto?> UpdateAddressAsync(string addressId, string userId, CreateUserAddressRequest req, CancellationToken ct)
    {
        var zip = Application.Services.AddressResolver.NormalizeZipCode(req.ZipCode);

        if (req.SetAsDefault)
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE user_address SET is_default = false WHERE user_id = {userId} AND id != {addressId}""", ct);

        var rows = await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE user_address
            SET label         = {req.Label},
                zip_code      = {zip},
                street        = {req.Street},
                number        = {req.Number},
                neighborhood  = {req.Neighborhood},
                city          = {req.City},
                state         = {req.State},
                complement    = {req.Complement},
                reference     = {req.Reference},
                is_default    = {req.SetAsDefault}
            WHERE id = {addressId} AND user_id = {userId}
        """, ct);

        if (rows == 0) return null;

        return new UserAddressDto(addressId, req.Label, zip, req.Street, req.Number,
            req.Neighborhood, req.City, req.State, req.Complement,
            req.Reference, req.SetAsDefault, null);
    }

    public async Task<bool> DeleteAddressAsync(string addressId, string userId, CancellationToken ct)
    {
        var rows = await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM user_address WHERE id = {addressId} AND user_id = {userId}
        """, ct);
        return rows > 0;
    }

    public async Task MarkAddressAsUsedAsync(string addressId, string userId, CancellationToken ct)
    {
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE user_address
            SET last_used_at = now()
            WHERE id = {addressId} AND user_id = {userId}
        """, ct);
    }

    private static object ToUserObject(SocialUserRow row, AddressDto? address = null) => new
    {
        id = row.Id, name = row.Name, email = row.Email, phone = row.Phone,
        role = row.Role, zoneId = row.ZoneId, createdAt = row.CreatedAt,
        provider = row.Provider, providerUserId = row.ProviderUserId,
        defaultAddress = address is not null ? (object)new
        {
            zipCode = address.ZipCode,
            street = address.Street,
            number = address.Number,
            neighborhood = address.Neighborhood,
            city = address.City,
            state = address.State,
            complement = address.Complement,
            reference = address.Reference
        } : null
    };

    // Internal record for raw SQL projection
    private sealed record AddressRow(
        string? ZipCode, string? Street, string? Number,
        string? Neighborhood, string? City, string? State,
        string? Complement, string? Reference);

    private sealed record UserAddressRow
    {
        public string Id { get; init; } = string.Empty;
        public string? Label { get; init; }
        public string ZipCode { get; init; } = string.Empty;
        public string Street { get; init; } = string.Empty;
        public string Number { get; init; } = string.Empty;
        public string Neighborhood { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string? Complement { get; init; }
        public string? Reference { get; init; }
        public bool IsDefault { get; init; }
        public DateTime? LastUsedAt { get; init; }
    }

    private sealed record AuthInfoRow
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public bool HasPassword { get; init; }
        public string? Provider { get; init; }
    }

    private sealed record VerificationInfoRow
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public bool EmailVerified { get; init; }
    }

    private sealed record SocialUserRow
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public string Role { get; init; } = string.Empty;
        public string? ZoneId { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? Provider { get; init; }
        public string? ProviderUserId { get; init; }
    }
}
