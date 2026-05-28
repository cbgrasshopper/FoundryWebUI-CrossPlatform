using FoundryWebUI.Services;

namespace FoundryWebUI.Endpoints;

public static class SystemPromptsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/system-prompts", GetSystemPrompts);
        app.MapGet("/api/system-prompts/{id}", GetSystemPrompt);
        app.MapPost("/api/system-prompts", CreateSystemPrompt);
        app.MapPut("/api/system-prompts/{id}", UpdateSystemPrompt);
        app.MapDelete("/api/system-prompts/{id}", DeleteSystemPrompt);
        app.MapPut("/api/system-prompts/{id}/default", SetDefaultPrompt);
    }

    private static IResult GetSystemPrompts(SystemPromptStore promptStore) =>
        Results.Ok(promptStore.GetAll());

    private static IResult GetSystemPrompt(SystemPromptStore promptStore, string id)
    {
        var prompt = promptStore.GetById(id);
        return prompt is null
            ? Results.NotFound(new { error = "Prompt not found" })
            : Results.Ok(prompt);
    }

    private static IResult CreateSystemPrompt(SystemPromptStore promptStore, SystemPromptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { error = "Name and content are required" });
        }
        var prompt = promptStore.Add(request.Name.Trim(), request.Content.Trim());
        return Results.Ok(prompt);
    }

    private static IResult UpdateSystemPrompt(SystemPromptStore promptStore, string id, SystemPromptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { error = "Name and content are required" });
        }
        var prompt = promptStore.Update(id, request.Name.Trim(), request.Content.Trim());
        return prompt is null
            ? Results.NotFound(new { error = "Prompt not found" })
            : Results.Ok(prompt);
    }

    private static IResult DeleteSystemPrompt(SystemPromptStore promptStore, string id) =>
        promptStore.Delete(id)
            ? Results.Ok(new { message = "Deleted" })
            : Results.NotFound(new { error = "Prompt not found" });

    private static IResult SetDefaultPrompt(SystemPromptStore promptStore, string id) =>
        promptStore.SetDefault(id)
            ? Results.Ok(new { message = "Default updated" })
            : Results.NotFound(new { error = "Prompt not found" });
}

public sealed class SystemPromptRequest
{
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
