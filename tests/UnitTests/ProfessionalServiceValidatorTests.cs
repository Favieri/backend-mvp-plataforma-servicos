using Application.DTOs;
using Application.Validation;
using Domain.Enums;
using Xunit;

namespace UnitTests;

public sealed class ProfessionalServiceValidatorTests
{
    private readonly CreateProfessionalServiceRequestValidator _validator = new();

    [Fact]
    public async Task ReservaDireta_WithPrecoAndDuration_IsValid()
    {
        var req = new CreateProfessionalServiceRequest(
            "p1", "s1", "Limpeza", 150m, null,
            DurationMinutes: 60,
            TipoContratacao: TipoContratacao.ReservaDireta);
        var result = await _validator.ValidateAsync(req);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ReservaDireta_WithNullPreco_IsInvalid()
    {
        var req = new CreateProfessionalServiceRequest(
            "p1", "s1", "Limpeza", null, null,
            DurationMinutes: 60,
            TipoContratacao: TipoContratacao.ReservaDireta);
        var result = await _validator.ValidateAsync(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Preco");
    }

    [Fact]
    public async Task ReservaDireta_WithNullDuration_IsInvalid()
    {
        var req = new CreateProfessionalServiceRequest(
            "p1", "s1", "Limpeza", 150m, null,
            DurationMinutes: null,
            TipoContratacao: TipoContratacao.ReservaDireta);
        var result = await _validator.ValidateAsync(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "DurationMinutes");
    }

    [Fact]
    public async Task Proposta_WithNullPreco_IsValid()
    {
        var req = new CreateProfessionalServiceRequest(
            "p1", "s1", "Consultoria", null, null,
            TipoContratacao: TipoContratacao.Proposta);
        var result = await _validator.ValidateAsync(req);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Proposta_WithNullDuration_IsValid()
    {
        var req = new CreateProfessionalServiceRequest(
            "p1", "s1", "Consultoria", null, null,
            DurationMinutes: null,
            TipoContratacao: TipoContratacao.Proposta);
        var result = await _validator.ValidateAsync(req);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("INVALIDO")]
    [InlineData("booking")]
    [InlineData("tier1")]
    public async Task InvalidTipoContratacao_IsRejected(string value)
    {
        var req = new CreateProfessionalServiceRequest(
            "p1", "s1", "Serviço", null, null,
            TipoContratacao: value);
        var result = await _validator.ValidateAsync(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "TipoContratacao");
    }

    [Fact]
    public async Task NullTipoContratacao_SkipsNewValidation()
    {
        // When tipoContratacao is null, no RESERVA_DIRETA/PROPOSTA rules apply
        var req = new CreateProfessionalServiceRequest(
            "p1", "s1", "Serviço", null, null,
            TipoContratacao: null);
        var result = await _validator.ValidateAsync(req);
        Assert.True(result.IsValid);
    }
}
