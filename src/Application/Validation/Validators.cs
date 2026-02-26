using Application.DTOs;
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

public sealed class CreatePaymentPreferenceRequestValidator : AbstractValidator<CreatePaymentPreferenceRequest>
{
    public CreatePaymentPreferenceRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.AmountCents).GreaterThan(0);
    }
}
