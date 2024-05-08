# Infinite Story

## Description

Infinite Story is a text-based adventure game where you can choose your own path. The story is infinite, and you can keep playing forever.
This is done through the use of aritificial intelligence, which generates the story as you play.

While I have set up a default story for you to start with, the AI will generate new content as you play, so you will never run out of story to explore.
You can also choose to define your own starting point, by providing a text file with the story you want to start with.

## Getting started

To get started, take a look at the `StoryPrompt.txt` file. This file contains notes that the AI uses to generate the start of the story. You can edit this file to change any aspect of the story.

### Running inference

You are able to either run the AI locally (through Ollama) or use the OpenAI API. To run the AI locally, you will need to install Ollama and llama3. To use the OpenAI API, you will need to set up an account and get an API key.

- Open AI
- Ollama

From there, you will have to choose a model to use. The default model is `gpt-3.5-turbo` for when using OpenAI, and `llama3` when running through Ollama- but you can choose any model that is supported by OpenAI or installed on Ollama (e.g. gpt-4, mistral, etc.)

Keep in mind, I store your OpenAI api key (somewhat insecurely) in the `.key` file at the root of the program. I welcome any pull requests that improve this security.

### Running the game

Just run InfiniteStory.exe, and follow the prompts to get set up.

## Contributing

If you would like to contribute to the project, feel free to fork the repository and make a pull request. I am always looking for ways to improve the game, and I welcome any contributions.
