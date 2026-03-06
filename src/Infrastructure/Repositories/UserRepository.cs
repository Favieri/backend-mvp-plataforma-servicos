using Application.Abstractions;
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

    public async Task<object> CreateAsync(string name, string email, string? phone, string role, string hashedPassword, string? zoneId, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        // senha is stored separately — use raw SQL to include it safely without adding it to the domain entity
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "User"(id, name, email, phone, role, senha, "zoneId", "createdAt")
            VALUES ({id}, {name}, {email}, {phone}, {role}, {hashedPassword}, {zoneId}, {now})
            """, ct);

        return new { id, name, email, phone, role, zoneId, createdAt = now };
    }
}
