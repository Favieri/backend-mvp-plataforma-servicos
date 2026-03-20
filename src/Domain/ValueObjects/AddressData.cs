namespace Domain.ValueObjects;

public sealed record AddressData(
    string ZipCode,
    string Street,
    string Number,
    string Neighborhood,
    string City,
    string State,
    string? Complement,
    string? Reference);
