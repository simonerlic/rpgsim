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
    private readonly string _narrativeSystemPrompt = 
        "You are the gamemaster for a text-based RPG. Your role is to craft and narrate an evolving story for the player, " +
        "responding to their actions and decisions. Focus on descriptive storytelling, rather than game mechanics or rules. " +
        "Give dialogue, descriptions, and reactions that immerse the player in the world you create." + 
        "You are given notes each turn that contain important details about the game world. Do not reveal these to the player.";

    private readonly string _notesSystemPrompt = 
        "Your job is to track crucial game elements such as player actions, relationships, inventory, and world state changes. " +
        "This information is used to maintain continuity and coherence in the game world, fostering an engaging " +
        "and interactive experience for the player. These notes are confidential and should not be revealed to the player. " +
        "Keep them organized and refer to them as needed to enhance the player's experience.";

    private readonly string _narrativeAiProvider;
    private readonly string _notesAiProvider;
    private readonly string _narrativeApiKey;
    private readonly string _notesApiKey;
    private readonly string _narrativeAiModel;
    private readonly string _notesAiModel;

    public ChatService(ChatHistoryService chatHistoryService, string narrativeAiProvider, string notesAiProvider, string? narrativeApiKey = null, string? notesApiKey = null, string? narrativeAiModel = null, string? notesAiModel = null)
    {
        _chatHistoryService = chatHistoryService;
        _httpClient = new HttpClient();
        _narrativeAiProvider = narrativeAiProvider;
        _notesAiProvider = notesAiProvider;
        _narrativeApiKey = narrativeApiKey ?? "";
        _notesApiKey = notesApiKey ?? "";
        _narrativeAiModel = narrativeAiModel ?? "";
        _notesAiModel = notesAiModel ?? "";
    }

    public async Task<string> GetResponse(string userInput, bool isNewSession = false)
    {
        string narrative = await GetNarrativeResponse(userInput);
        string notes = await GetNotesResponse(narrative, userInput);

        SaveStateToFile(notes); // Save the generated notes

        return narrative; // Return the narrative to the user
    }

    private async Task<string> GetNarrativeResponse(string userInput)
    {
        StringBuilder promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(_narrativeSystemPrompt);
        promptBuilder.AppendLine(GetPreviousDialogues());
        promptBuilder.AppendLine(userInput);

        var prompt = promptBuilder.ToString();

        if (_narrativeAiProvider == "OpenAI")
        {
            return await GetOpenAINarrativeResponse(prompt);
        }
        else
        {
            return await GetOllamaNarrativeResponse(prompt);
        }
    }

    private async Task<string> GetNotesResponse(string narrative, string userInput)
    {
        StringBuilder promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(_notesSystemPrompt);
        promptBuilder.AppendLine(narrative);
        promptBuilder.AppendLine(userInput);
        promptBuilder.AppendLine("Notes:");
        promptBuilder.AppendLine(File.ReadAllText(_stateFilePath));

        var prompt = promptBuilder.ToString();

        if (_notesAiProvider == "OpenAI")
        {
            return await GetOpenAINotesResponse(prompt);
        }
        else
        {
            return await GetOllamaNotesResponse(prompt);
        }
    }

    private async Task<string> GetOllamaNarrativeResponse(string prompt)
    {
        var requestData = new
        {
            model = !string.IsNullOrEmpty(_narrativeAiModel) ? _narrativeAiModel : "llama3",
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

    private async Task<string> GetOpenAINarrativeResponse(string prompt)
    {
        // Construct the JSON message history including the system, previous dialogues, and user input
        var messages = new List<object>
        {
            new { role = "system", content = _narrativeSystemPrompt }
        };

        var dialogues = _chatHistoryService.GetHistory().TakeLast(20).ToList(); // Get the last 20 entries (10 turns)
        foreach (var entry in dialogues)
        {
            messages.Add(new { role = "user", content = entry.User });
            messages.Add(new { role = "assistant", content = entry.AI });
        }
        messages.Add(new { role = "user", content = prompt });

        var postData = new
        {
            model = !string.IsNullOrEmpty(_narrativeAiModel) ? _narrativeAiModel : "gpt-3.5-turbo",
            messages = messages
        };

        // Configure HTTP request headers
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _narrativeApiKey);
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

    private async Task<string> GetOllamaNotesResponse(string prompt)
    {
        var requestData = new
        {
            model = !string.IsNullOrEmpty(_notesAiModel) ? _notesAiModel : "llama3",
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

    private async Task<string> GetOpenAINotesResponse(string prompt)
    {
        // Construct the JSON message history including the system, previous dialogues, user input, and notes
        var messages = new List<object>
        {
            new { role = "system", content = _notesSystemPrompt }
        };

        var dialogues = _chatHistoryService.GetHistory().TakeLast(20).ToList(); // Get the last 20 entries (10 turns)
        foreach (var entry in dialogues)
        {
            messages.Add(new { role = "user", content = entry.User });
            messages.Add(new { role = "assistant", content = entry.AI });
        }
        messages.Add(new { role = "user", content = prompt });
        messages.Add(new { role = "assistant", content = "Notes:" });
        messages.Add(new { role = "assistant", content = File.ReadAllText(_stateFilePath) });

        var postData = new
        {
            model = !string.IsNullOrEmpty(_notesAiModel) ? _notesAiModel : "gpt-3.5-turbo",
            messages = messages
        };

        // Configure HTTP request headers
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _notesApiKey);
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
            dialogues.AppendLine($"Game Master: {entry.AI}");
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
