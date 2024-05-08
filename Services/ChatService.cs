using Newtonsoft.Json;
using System.Text;
using System.Net.Http.Json;
using System.IO;

public class ChatService
{
    private readonly HttpClient _httpClient;
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
    "should NEVER be revealed to the player. If asked about them, play coy. Your goal is to maintain continuity and coherence in the game world, " +
    "fostering an engaging and interactive experience for the player. Don't specifically mention anything about the game being an RPG, text-based, " +
    "or gamemastered.";
    private readonly string _aiProvider;
    private string _endpoint;
    private readonly string _apiKey;
    private readonly string _aiModel;

    public ChatService(ChatHistoryService chatHistoryService, string aiProvider, string? apiKey = null, string? aiModel = null)
    {
        _chatHistoryService = chatHistoryService;
        _httpClient = new HttpClient();
        _aiProvider = aiProvider;
        _apiKey = apiKey ?? "";
        _aiModel = aiModel ?? "";
        _endpoint = _aiProvider == "OpenAI" ? "https://api.openai.com/v1/chat/completions" : "http://localhost:11434/api/generate";
    }

    public async Task<string> GetResponse(string userInput)
    {
        string previousDialogues = GetPreviousDialogues();
        string stateNotes = File.ReadAllText(_stateFilePath);

        var prompt = $"{_systemPrompt}\n{previousDialogues}\n{userInput}\nGAME NOTES:\n{stateNotes}";
        if (_aiProvider == "OpenAI")
        {
            return await GetOpenAIResponse(prompt);
        }
        else
        {
            return await GetOllamaResponse(prompt);
        }
    }

    private async Task<string> GetOllamaResponse(string prompt)
    {
        var requestData = new
        {
            model = !string.IsNullOrEmpty(_aiModel) ? _aiModel : "llama3",
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

    private async Task<string> GetOpenAIResponse(string userInput)
    {
        // Construct the JSON message history including the system, previous dialogues, and user input
        var messages = new List<object>
        {
            new { role = "system", content = _systemPrompt }
        };

        var dialogues = _chatHistoryService.GetHistory().TakeLast(20).ToList(); // Get the last 20 entries (10 turns)
        foreach (var entry in dialogues)
        {
            messages.Add(new { role = "user", content = entry.User });
            messages.Add(new { role = "assistant", content = entry.AI });
        }
        messages.Add(new { role = "user", content = userInput });

        var postData = new
        {
            model = !string.IsNullOrEmpty(_aiModel) ? _aiModel : "gpt-3.5-turbo",
            messages = messages
        };

        // Configure HTTP request headers
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", postData);
            if (response.IsSuccessStatusCode)
            {
                var apiResponse = await response.Content.ReadFromJsonAsync<OpenAIResponse>();
                var lastResponse = apiResponse!.choices.Last().message.content;

                // Handle separator for notes
                var parts = lastResponse.Split(new[] { _separator }, 2, StringSplitOptions.RemoveEmptyEntries);
                var textResponse = parts[0].Trim();
                if (parts.Length > 1)
                {
                    SaveStateToFile(parts[1].Trim()); // Save the game notes
                }
                return textResponse;
            }
            else
            {
                return "Failed to get response from OpenAI: " + response.ReasonPhrase;
            }
        }
        catch (Exception ex)
        {
            return "Error contacting OpenAI: " + ex.Message;
        }
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

    // OpenAI Response class to deserialize the response
    public class OpenAIResponse
    {
        public required Choice[] choices { get; set; }
        public class Choice
        {
            public required Message message { get; set; }
        }
        public class Message
        {
            public required string content { get; set; }
        }
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
