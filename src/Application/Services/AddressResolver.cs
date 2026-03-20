using System.Text.RegularExpressions;
using Application.DTOs;
using Domain.ValueObjects;

namespace Application.Services;

public static class AddressResolver
{
    public static (AddressData? address, string? error) Resolve(
        bool useDefaultAddress,
        AddressDto? serviceAddress,
        AddressDto? defaultAddress)
    {
        if (useDefaultAddress)
        {
            if (defaultAddress is null)
                return (null, "Cliente não possui endereço padrão cadastrado");

            var (valid, err) = Validate(defaultAddress);
            if (!valid)
                return (null, err);

            return (ToAddressData(defaultAddress), null);
        }

        if (serviceAddress is null)
            return (null, "Endereço do serviço é obrigatório para concluir o pedido");

        var (isValid, validationError) = Validate(serviceAddress);
        if (!isValid)
            return (null, validationError);

        return (ToAddressData(serviceAddress), null);
    }

    public static (bool valid, string? error) Validate(AddressDto address)
    {
        if (string.IsNullOrWhiteSpace(address.ZipCode))
            return (false, "Campo 'zipCode' é obrigatório");
        if (!IsValidZipCode(address.ZipCode))
            return (false, "CEP inválido");
        if (string.IsNullOrWhiteSpace(address.Street))
            return (false, "Campo 'street' é obrigatório");
        if (string.IsNullOrWhiteSpace(address.Number))
            return (false, "Campo 'number' é obrigatório");
        if (string.IsNullOrWhiteSpace(address.Neighborhood))
            return (false, "Campo 'neighborhood' é obrigatório");
        if (string.IsNullOrWhiteSpace(address.City))
            return (false, "Campo 'city' é obrigatório");
        if (string.IsNullOrWhiteSpace(address.State))
            return (false, "Campo 'state' é obrigatório");

        return (true, null);
    }

    public static bool IsValidZipCode(string zipCode)
    {
        var normalized = zipCode.Replace("-", "").Replace(".", "").Trim();
        return Regex.IsMatch(normalized, @"^\d{8}$");
    }

    public static string NormalizeZipCode(string zipCode)
        => zipCode.Replace("-", "").Replace(".", "").Trim();

    public static AddressData ToAddressData(AddressDto dto)
        => new(
            ZipCode: NormalizeZipCode(dto.ZipCode),
            Street: dto.Street.Trim(),
            Number: dto.Number.Trim(),
            Neighborhood: dto.Neighborhood.Trim(),
            City: dto.City.Trim(),
            State: dto.State.Trim(),
            Complement: dto.Complement?.Trim(),
            Reference: dto.Reference?.Trim());

    public static AddressDto ToDto(AddressData data)
        => new(data.ZipCode, data.Street, data.Number,
               data.Neighborhood, data.City, data.State,
               data.Complement, data.Reference);
}
