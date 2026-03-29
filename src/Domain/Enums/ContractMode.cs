namespace Domain.Enums;

public static class ContractMode
{
    public const string BookingDirect = "booking_direct";
    public const string QuoteRequired = "quote_required";
    public const string Both = "both";

    // Simplified API-facing values (frontend contract)
    public const string Booking = "booking";
    public const string Proposal = "proposal";
}
