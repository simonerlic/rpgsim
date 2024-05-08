using Newtonsoft.Json;

public class InventoryItem
{
    public required string Name { get; set; }
    public int Quantity { get; set; }
}

public class GameState
{
    public List<InventoryItem> Inventory { get; set; } = new List<InventoryItem>();
    public string? Location { get; set; }
    public Dictionary<string, int> Relationships { get; set; } = new Dictionary<string, int>();
}

public class GameService
{
    public GameState CurrentState { get; private set; } = new GameState();

    public void UpdateStateFromAI(string json)
    {
        try
        {
            var newState = JsonConvert.DeserializeObject<GameState>(json);
            if (newState != null)
            {
                CurrentState = newState;
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to update game state from AI: {ex.Message}");
        }
    }
}

