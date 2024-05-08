using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;

namespace TextBasedRPG
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var serviceProvider = new ServiceCollection()
                .AddSingleton<ChatService>()
                .AddSingleton<ChatHistoryService>()
                .BuildServiceProvider();
            var aiService = serviceProvider.GetService<ChatService>();
            var chatHistory = serviceProvider.GetService<ChatHistoryService>();

            initNotes();

            var console = AnsiConsole.Console;
            console.Clear();
            console.Write(new Panel("[bold yellow]Welcome to the game![/] \nTell me when you are ready to start. Type 'exit' to quit.")
                .RoundedBorder()
                .BorderColor(Color.Yellow));

            string input = "";
            while (input.ToLower() != "exit")
            {
                input = console.Prompt(new TextPrompt<string>("[green]>[/]")
                    .PromptStyle("aqua"));

                if (input.ToLower() == "exit")
                    break;

                string response = await AnsiConsole.Status()
                    .AutoRefresh(true)
                    .Spinner(Spinner.Known.Default)
                    .StartAsync("[yellow]Getting response...[/]", async ctx =>
                    {
                        return await aiService!.GetResponse(input);
                    });

                chatHistory!.AddToHistory(input, response);
                console.Clear();
                console.MarkupLine($"[white]{response}[/]");
                console.WriteLine();
            }
        }

        static void initNotes()
        {
            var stateFilePath = Path.Combine(Environment.CurrentDirectory, "GameStateNote.txt");
            File.WriteAllText(stateFilePath,
    @"Location: Market Square
- Description: The central hub of commerce and interaction in the city, bustling with activity.
- Characters:
  - City Watch: Heavily guarding the city gate, vigilant and alert.
  - Shady Characters: A group near the fountain, speaking in hushed tones, possibly plotting.
- Points of Interest:
  - Fountain: Meeting point for shady characters.
  - City Gate: Main entrance and exit, heavily guarded.
- Vendors:
  - Exotic Goods Sellers: Eager to sell exotic items.
  - Skill Buyers: Interested in buying services or skills from adventurers.");

        }
    }
}
