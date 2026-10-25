namespace Rroms.Web.Models.ViewModels;

/// <summary>Kitchen performance for one day in Malaysia time, plus the week before it.</summary>
public class KitchenStats
{
    public DateOnly Day { get; set; }
    /// <summary>Orders that went through the kitchen that day, including drinks-only orders.</summary>
    public int Orders { get; set; }
    public int DrinksOnly { get; set; }
    /// <summary>Average minutes from joining the line to cooking starting. Cooked orders only.</summary>
    public double? AverageWaitMinutes { get; set; }
    public double? AverageCookMinutes { get; set; }
    /// <summary>Average minutes food waited at the pass before staff served it.</summary>
    public double? AverageReadyToServedMinutes { get; set; }
    /// <summary>Malaysia hour (0-23) with the most orders starting, or null on a day with none.</summary>
    public int? BusiestHour { get; set; }
    /// <summary>Orders starting in each Malaysia hour, index 0 is midnight.</summary>
    public int[] OrdersByHour { get; set; } = new int[24];
    /// <summary>The seven days ending on <see cref="Day"/>, oldest first.</summary>
    public List<KitchenDay> Week { get; set; } = new List<KitchenDay>();
}

/// <summary>One day in the weekly kitchen trend.</summary>
public record KitchenDay(DateOnly Day, int Orders, double? AverageWaitMinutes);

/// <summary>Chatbot use over a period, by topic and by where the answer came from. No question text is kept.</summary>
public class ChatInsights
{
    public int Days { get; set; }
    public int Total { get; set; }
    public int Local { get; set; }
    public int Ai { get; set; }
    public int Fallback { get; set; }
    /// <summary>Most asked first.</summary>
    public List<(string Topic, int Count)> Topics { get; set; } = new List<(string Topic, int Count)>();
    /// <summary>Questions per Malaysia day, oldest first.</summary>
    public List<(DateOnly Day, int Count)> PerDay { get; set; } = new List<(DateOnly Day, int Count)>();
}
