using Application.DTOs;
using Domain.Enums;
using FluentValidation;

namespace Application.Validation;

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.ServiceId).NotEmpty();
    }
}

public sealed class CreateProfessionalServiceRequestValidator : AbstractValidator<CreateProfessionalServiceRequest>
{
    public CreateProfessionalServiceRequestValidator()
    {
        RuleFor(x => x.ProfessionalId).NotEmpty();
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.NomeServico).NotEmpty();

        RuleFor(x => x.TipoContratacao)
            .Must(v => v is null || v == TipoContratacao.ReservaDireta || v == TipoContratacao.Proposta)
            .WithMessage("tipoContratacao inválido. Valores aceitos: RESERVA_DIRETA, PROPOSTA.");

        When(x => x.TipoContratacao == TipoContratacao.ReservaDireta, () =>
        {
            RuleFor(x => x.Preco)
                .NotNull()
                .GreaterThan(0m)
                .WithMessage("precoBase é obrigatório e deve ser maior que zero para tipoContratacao 'RESERVA_DIRETA'.");
            RuleFor(x => x.DurationMinutes)
                .NotNull()
                .GreaterThan(0)
                .WithMessage("durationMinutes é obrigatório e deve ser maior que zero para tipoContratacao 'RESERVA_DIRETA'.");
        });
    }
}

