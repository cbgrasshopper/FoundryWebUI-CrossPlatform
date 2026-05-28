namespace FoundryWebUI.Endpoints;

public static class EndpointRegistry
{
    public static WebApplication MapEndpoints(this WebApplication app)
    {
        StatusEndpoints.Map(app);
        ModelsEndpoints.Map(app);
        ChatEndpoints.Map(app);
        LogsEndpoints.Map(app);
        SettingsEndpoints.Map(app);
        SystemPromptsEndpoints.Map(app);
        return app;
    }
}
