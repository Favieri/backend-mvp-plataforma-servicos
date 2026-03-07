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
            .SqlQueryRaw<AuthRow>(
                """SELECT id AS "Id", name AS "Name", email AS "Email", phone AS "Phone", role AS "Role", senha AS "Senha", "createdAt" AS "CreatedAt" FROM "User" WHERE email = {0}""",
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
            createdAt = row.CreatedAt
        };
    }

    private sealed class AuthRow
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public string Role { get; init; } = string.Empty;
        public string Senha { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }
}
