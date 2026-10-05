using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Infrastructure.Simulation;
using ProductionIncident.LlmOps.Tracing;

namespace ProductionIncident.Infrastructure.AI;

public sealed class OpenAIConnectionOptions
{
    public const string SectionName = "AI:OpenAI";

    /// <summary>
    /// OpenAI-compatible endpoint. For Azure OpenAI / Azure AI Foundry use the v1 endpoint:
    /// https://{resource}.openai.azure.com/openai/v1/ . Leave empty for api.openai.com.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>Read from Key Vault / user secrets / environment — never committed, never sent to the model.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>Creates the IChatClient used by every agent, wrapped with OpenTelemetry (gen_ai semantic conventions).</summary>
public static class ChatClientFactory
{
    public static IChatClient Create(AgentModelOptions model, OpenAIConnectionOptions connection, ILoggerFactory loggerFactory)
    {
        IChatClient inner = model.Provider.ToLowerInvariant() switch
        {
            "scripted" => new ScriptedChatClient(),
            "openai" or "azureopenai" => CreateOpenAI(model, connection),
            _ => throw new InvalidOperationException($"Unknown AI provider '{model.Provider}'. Use Scripted, OpenAI or AzureOpenAI."),
        };

        return new ChatClientBuilder(inner)
            .UseOpenTelemetry(loggerFactory, IncidentTelemetry.SourceName, otel => otel.EnableSensitiveData = false)
            .Build();
    }

    private static IChatClient CreateOpenAI(AgentModelOptions model, OpenAIConnectionOptions connection)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            throw new InvalidOperationException("AI:OpenAI:ApiKey is required for the OpenAI/AzureOpenAI provider (use user-secrets or Key Vault).");
        }

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(connection.Endpoint))
        {
            options.Endpoint = new Uri(connection.Endpoint);
        }

        var client = new OpenAIClient(new ApiKeyCredential(connection.ApiKey), options);
        return client.GetChatClient(model.Model).AsIChatClient();
    }
}
