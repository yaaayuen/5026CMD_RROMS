namespace Rroms.Web.Options;

/// <summary>
/// Booking rules agreed in Sprint 0, bound from the "ReservationRules" configuration section.
/// Keeping these numbers in one place means a rule change is a one-line configuration edit rather than a hunt through the code.
/// </summary>
public class ReservationRulesOptions
{
    //Name of the configuration section this class is bound to.
    public const string SectionName = "ReservationRules";

    //How long a table is held for each booking. Agreed value: 120 minutes.
    public int VisitDurationMinutes { get; set; } = 120;

    //Booking buffer time until customer arrives. Agreed value: 30.
    public int SlotIntervalMinutes { get; set; } = 30;

    //How far ahead a customer may book. Agreed value: 90 days.
    public int MaxDaysAhead { get; set; } = 90;

    //Smallest allowed party size.
    public int MinPartySize { get; set; } = 1;

    //Largest allowed party size. Agreed value: 20.
    public int MaxPartySize { get; set; } = 20;
}
