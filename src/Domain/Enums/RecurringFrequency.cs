namespace Domain.Enums;

public static class RecurringFrequency
{
    public const string Weekly   = "weekly";
    public const string Biweekly = "biweekly";
    public const string Monthly  = "monthly";

    /// <summary>Returns the number of days between occurrences for a given frequency.</summary>
    public static int ToDays(string frequency) => frequency switch
    {
        Weekly   => 7,
        Biweekly => 14,
        Monthly  => 30,
        _        => 30
    };
}
