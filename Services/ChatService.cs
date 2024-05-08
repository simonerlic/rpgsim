using Newtonsoft.Json;
using System.Text;
using System.Net.Http.Json;
using System.IO;

public class ChatService
{
    private readonly HttpClient _httpClient;
    private readonly GameService _gameService;
    private readonly ChatHistoryService _chatHistoryService;
    private readonly string _stateFilePath = "GameStateNote.txt";
    private readonly string _separator = "||";
    private readonly string _systemPrompt =
    "You are the gamemaster for a text-based RPG. Your role is to craft and narrate an evolving story for the player, " +
    "responding to their actions and decisions. At the end of each turn, you must provide two types of information: " +
    "1. A narrative response to the player's actions, guiding them through the game world without suggesting specific actions. " +
    "2. Private notes that track crucial game elements such as player actions, relationships, inventory, and world state changes. " +
    "Use two pipe characters: || to separate your public narrative from your private notes. The separator should be used only once per turn. " +
    "Ensure all relevant details are included in your notes as they are cleared each turn. These notes are confidential and " +
    "should not be revealed to the player. Your goal is to maintain continuity and coherence in the game world, fostering an engaging " +
    "and interactive experience for the player.";


    public ChatService(GameService gameService, ChatHistoryService chatHistoryService)
    {
        _gameService = gameService;
        _chatHistoryService = chatHistoryService;
        _httpClient = new HttpClient();
    }

    public async Task<string> GetResponse(string userInput)
    {
        string previousDialogues = GetPreviousDialogues();
        string stateNotes = File.ReadAllText(_stateFilePath);

        var prompt = $"{_systemPrompt}\n{previousDialogues}\n{userInput}\nGAME NOTES:\n{stateNotes}";
        var requestData = new
        {
            model = "llama3",
            prompt = prompt,
            stream = true
        };

        var response = await _httpClient.PostAsJsonAsync("http://localhost:11434/api/generate", requestData);
        if (response.IsSuccessStatusCode)
        {
            var stringBuilder = new StringBuilder();
            var stream = await response.Content.ReadAsStreamAsync();
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var apiResponse = JsonConvert.DeserializeObject<OllamaResponse>(line);
                        if (apiResponse != null)
                        {
                            stringBuilder.Append(apiResponse.response);
                            if (apiResponse.done)
                            {
                                var lastResponse = stringBuilder.ToString();
                                var parts = lastResponse.Split(new[] { _separator }, 2, StringSplitOptions.RemoveEmptyEntries);
                                var textResponse = parts[0].Trim();
                                if (parts.Length > 1)
                                {
                                    SaveStateToFile(parts[1].Trim());
                                }
                                return textResponse;
                            }
                        }
                    }
                }
            }
        }

        return "Failed to get response from AI";
    }

    private string GetPreviousDialogues()
    {
        var history = _chatHistoryService.GetHistory().TakeLast(10).ToList();  // Get the last 10 entries (5 turns)
        StringBuilder dialogues = new StringBuilder();
        foreach (var entry in history)
        {
            dialogues.AppendLine($"User: {entry.User}");
            dialogues.AppendLine($"AI: {entry.AI}");
        }
        return dialogues.ToString();
    }

    private void SaveStateToFile(string stateNote)
    {
        File.WriteAllText(_stateFilePath, stateNote);
    }

    private class OllamaResponse
    {
        public required string model { get; set; }
        public DateTime created_at { get; set; }
        public required string response { get; set; }
        public bool done { get; set; }
        public required int[] context { get; set; }
    }
}



public class ChatHistoryService
{
    private List<(string User, string AI)> history = new List<(string, string)>();

    public void AddToHistory(string userInput, string aiResponse)
    {
        history.Add((userInput, aiResponse));
    }

    public IEnumerable<(string User, string AI)> GetHistory()
    {
        return history;
    }
}
