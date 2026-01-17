using System.Text.Json;
using ClaudeCodeAzureGptProxy.Models;
using ClaudeCodeAzureGptProxy.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaudeCodeAzureGptProxy.Tests;

public sealed class ConversionTests
{
    [Fact]
    public void ConvertAnthropicToAzure_MapsSystemAndToolChoice()
    {
        var request = new MessagesRequest
        {
            Model = "azure/my-deployment",
            MaxTokens = 256,
            Temperature = 0.2,
            System = new[]
            {
                new Dictionary<string, object?> { ["type"] = "text", ["text"] = "system prompt" }
            },
            ToolChoice = new ToolChoice { Type = "tool", Name = "calc" },
            Tools = new List<Tool>
            {
                new()
                {
                    Name = "calc",
                    Description = "calculator",
                    InputSchema = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["value"] = new Dictionary<string, object?> { ["type"] = "string" }
                        }
                    }
                }
            },
            Messages = new List<Message>
            {
                new()
                {
                    Role = "user",
                    Content = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "text", ["text"] = "Hi" }
                    }
                }
            }
        };

        var payload = AnthropicConversion.ConvertAnthropicToAzure(
            request,
            NullLogger.Instance,
            new ClaudeCodeAzureGptProxy.Infrastructure.NormalizedAzureOpenAiOptions());

        Assert.Equal("azure/my-deployment", payload["model"]);
        Assert.Equal(256, payload["max_tokens"]);
        Assert.Equal(0.2, payload["temperature"]);

        var messages = Assert.IsType<List<Dictionary<string, object?>>>(payload["messages"]);
        Assert.Equal("system", messages[0]["role"]);
        Assert.Equal("system prompt", messages[0]["content"]);

        var toolChoice = payload["tool_choice"] as Dictionary<string, object?>;
        Assert.NotNull(toolChoice);
        var function = toolChoice!["function"] as Dictionary<string, object?>;
        Assert.NotNull(function);
        Assert.Equal("calc", function!["name"]);
    }

    [Fact]
    public void ConvertAzureToAnthropic_MapsToolCalls()
    {
        var request = new MessagesRequest
        {
            Model = "azure/my-deployment",
            MaxTokens = 128,
            Messages = new List<Message>()
        };

        var azureResponse = new Dictionary<string, object?>
        {
            ["id"] = "msg_123",
            ["choices"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["finish_reason"] = "tool_calls",
                    ["message"] = new Dictionary<string, object?>
                    {
                        ["content"] = "",
                        ["tool_calls"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["id"] = "tool_1",
                                ["function"] = new Dictionary<string, object?>
                                {
                                    ["name"] = "calc",
                                    ["arguments"] = "{\"value\":\"2+2\"}"
                                }
                            }
                        }
                    }
                }
            },
            ["usage"] = new Dictionary<string, object?>
            {
                ["prompt_tokens"] = 10,
                ["completion_tokens"] = 5
            }
        };

        var response = AnthropicConversion.ConvertAzureToAnthropic(azureResponse, request, NullLogger.Instance);

        Assert.Equal("msg_123", response.Id);
        Assert.Equal("tool_use", response.StopReason);
        Assert.Equal(10, response.Usage.InputTokens);
        Assert.Equal(5, response.Usage.OutputTokens);
        Assert.Contains(response.Content, block =>
            block["type"]?.ToString() == "tool_use" &&
            block["name"]?.ToString() == "calc");
    }

    [Fact]
    public void Streaming_EmitsRequiredEvents()
    {
        var request = new MessagesRequest
        {
            Model = "azure/my-deployment",
            MaxTokens = 16,
            Messages = new List<Message>()
        };

        var stream = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["choices"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["delta"] = new Dictionary<string, object?> { ["content"] = "Hi" },
                        ["finish_reason"] = "stop"
                    }
                }
            }
        };

        var enumerator = SseStreaming.HandleStreaming(stream.ToAsyncEnumerable(), request, NullLogger.Instance)
            .ToEnumerable();

        var output = string.Join("", enumerator);
        Assert.Contains("message_start", output);
        Assert.Contains("content_block_start", output);
        Assert.Contains("content_block_delta", output);
        Assert.Contains("message_stop", output);
        Assert.Contains("data: [DONE]", output);
    }
}
