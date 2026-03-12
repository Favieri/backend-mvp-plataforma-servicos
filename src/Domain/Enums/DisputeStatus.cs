namespace Domain.Enums;

public static class DisputeStatus
{
    public const string Opened                 = "opened";
    public const string ProfessionalResponded  = "professional_responded";
    public const string Mediating              = "mediating";
    public const string Resolved               = "resolved";
    public const string Closed                 = "closed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Opened, ProfessionalResponded, Mediating, Resolved, Closed
    };

    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>
    {
        Resolved, Closed
    };
}
