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

    public async Task<object> FindOrCreateSocialUserAsync(string provider, string providerUserId, string email, string name, CancellationToken ct)
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
            return ToUserObject(existing);

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
            return ToUserObject(byEmail);
        }

        // 3. Create new user
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "User"(id, name, email, phone, role, senha, "zoneId", "createdAt", provider, provider_user_id)
            VALUES ({id}, {name}, {email}, {(string?)null}, {"cliente"}, {(string?)null}, {(string?)null}, {now}, {provider}, {providerUserId})
            """, ct);

        return new
        {
            id, name, email, phone = (string?)null, role = "cliente",
            zoneId = (string?)null, createdAt = now,
            provider, providerUserId,
            defaultAddress = (object?)null
        };
    }

    private static object ToUserObject(SocialUserRow row) => new
    {
        id = row.Id, name = row.Name, email = row.Email, phone = row.Phone,
        role = row.Role, zoneId = row.ZoneId, createdAt = row.CreatedAt,
        provider = row.Provider, providerUserId = row.ProviderUserId
    };

    // Internal record for raw SQL projection
    private sealed record AddressRow(
        string? ZipCode, string? Street, string? Number,
        string? Neighborhood, string? City, string? State,
        string? Complement, string? Reference);

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
