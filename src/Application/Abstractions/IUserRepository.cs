using Application.DTOs;
using Domain.Entities;

namespace Application.Abstractions;

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
    Task<bool> ZoneExistsAndActiveAsync(string zoneId, CancellationToken ct);
    Task<object> CreateAsync(string name, string email, string? phone, string role, string hashedPassword, string? zoneId, CancellationToken ct, AddressDto? defaultAddress = null);
    Task<AddressDto?> GetDefaultAddressAsync(string userId, CancellationToken ct);
    Task UpdateDefaultAddressAsync(string userId, AddressDto address, CancellationToken ct);
    Task<object> FindOrCreateSocialUserAsync(string provider, string providerUserId, string email, string name, CancellationToken ct);
}
