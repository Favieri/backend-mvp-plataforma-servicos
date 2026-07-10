using Application.DTOs;
using Domain.Entities;

namespace Application.Abstractions;

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
    Task<bool> ZoneExistsAndActiveAsync(string zoneId, CancellationToken ct);
    Task<bool> AnyAdminExistsAsync(CancellationToken ct);
    Task<object> CreateAsync(string name, string email, string? phone, string role, string hashedPassword, string? zoneId, CancellationToken ct, AddressDto? defaultAddress = null);
    Task<AddressDto?> GetDefaultAddressAsync(string userId, CancellationToken ct);
    Task UpdateDefaultAddressAsync(string userId, AddressDto address, CancellationToken ct);
    Task<(object User, bool IsNewUser)> FindOrCreateSocialUserAsync(string provider, string providerUserId, string email, string name, CancellationToken ct);
    Task<object?> GetByIdAsync(string userId, CancellationToken ct);
    Task UpdateUserAsync(string userId, string? name, string? phone, string? zoneId, CancellationToken ct);

    // ─── Múltiplos endereços (PRD-18a) ────────────────────────────────────────
    Task<IReadOnlyList<UserAddressDto>> GetAddressesAsync(string userId, CancellationToken ct);
    Task<UserAddressDto> CreateAddressAsync(string userId, CreateUserAddressRequest req, CancellationToken ct);
    Task<UserAddressDto?> UpdateAddressAsync(string addressId, string userId, CreateUserAddressRequest req, CancellationToken ct);
    Task<bool> DeleteAddressAsync(string addressId, string userId, CancellationToken ct);
    Task MarkAddressAsUsedAsync(string addressId, string userId, CancellationToken ct);
}
