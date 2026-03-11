namespace Domain.Entities;

public class ServiceTier
{
    public int Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string Code { get; private set; } = default!;
    public bool AllowBookingDirect { get; private set; }
    public bool RequiresProposal { get; private set; }
    public bool RequiresChat { get; private set; }
    public string[] AllowedPriceFormats { get; private set; } = [];
    public int DefaultSignalPercent { get; private set; }
    public int MaxInstallments { get; private set; }
    public string? CancellationRules { get; private set; }

    private ServiceTier() { }
}
