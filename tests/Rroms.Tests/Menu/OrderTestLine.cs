namespace Rroms.Tests.Menu;

/// <summary>One dish and how many portions of it a test order contains. Owner: YJ.</summary>
/// <param name="DishId">The dish's Id.</param>
/// <param name="Quantity">How many portions.</param>
public record OrderTestLine(int DishId, int Quantity);
