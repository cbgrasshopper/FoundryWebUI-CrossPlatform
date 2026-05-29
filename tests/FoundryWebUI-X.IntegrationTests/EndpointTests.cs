using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FoundryWebUI.IntegrationTests.Helpers;

namespace FoundryWebUI.IntegrationTests;

public class EndpointTests
{
    [Test]
    public async Task SystemInfo_ReturnsTotalRam()
    {
        using var factory = new FoundryWebUIFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/system-info");

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("totalRamMb").GetDouble() > 0).IsTrue();
    }

    [Test]
    public async Task Status_ReflectsFoundryAvailability()
    {
        using var factory = new FoundryWebUIFactory();
        factory.FoundryStub.When(HttpMethod.Get, "/openai/status", HttpStatusCode.OK, "{}");
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/status");

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var arr = await resp.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(arr.GetArrayLength()).IsEqualTo(1);
        await Assert.That(arr[0].GetProperty("isAvailable").GetBoolean()).IsTrue();
        await Assert.That(arr[0].GetProperty("provider").GetString()).IsEqualTo("foundry");
    }

    [Test]
    public async Task Models_MergesCatalogAndLoaded()
    {
        using var factory = new FoundryWebUIFactory();
        factory.FoundryStub
            .When(HttpMethod.Get, "/foundry/list", HttpStatusCode.OK, """
                [
                  { "name": "phi-3.5-mini", "displayName": "Phi-3.5 Mini", "fileSizeMb": 1024,
                    "publisher": "Microsoft", "runtime": {"deviceType":"cpu"} }
                ]
                """)
            .When(HttpMethod.Get, "/openai/models", HttpStatusCode.OK, """["mistral-7b"]""")
            .When(HttpMethod.Get, "/openai/loadedmodels", HttpStatusCode.OK, """["mistral-7b"]""");

        using var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/models");

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var arr = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var names = new List<string>();
        for (var i = 0; i < arr.GetArrayLength(); i++)
        {
            names.Add(arr[i].GetProperty("id").GetString() ?? "");
        }
        await Assert.That(names).Contains("mistral-7b");
        await Assert.That(names).Contains("phi-3.5-mini");
    }

    [Test]
    public async Task DeleteModel_ReturnsErrorWhenStubReturnsFailure()
    {
        using var factory = new FoundryWebUIFactory();
        factory.FoundryStub
            .When(HttpMethod.Get, "/openai/unload/", HttpStatusCode.OK, "{}")
            .When(HttpMethod.Get, "/openai/status", HttpStatusCode.OK, """{"modelDirPath":"/non/existent"}""");

        using var client = factory.CreateClient();
        var resp = await client.DeleteAsync("/api/models/some-model");

        await Assert.That((int)resp.StatusCode >= 500).IsTrue();
    }

    [Test]
    public async Task Reconnect_DelegatesToProvider()
    {
        using var factory = new FoundryWebUIFactory();
        factory.FoundryStub.When(HttpMethod.Get, "/openai/status", HttpStatusCode.OK, "{}");

        using var client = factory.CreateClient();
        var resp = await client.PostAsync("/api/reconnect", new StringContent(""));

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var status = await resp.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(status.GetProperty("isAvailable").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task Logs_FoundryEndpoint_ReturnsOk()
    {
        using var factory = new FoundryWebUIFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/logs/foundry?lines=50");
        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(payload.GetProperty("source").GetString()).IsEqualTo("foundry");
    }

    [Test]
    public async Task Logs_UnknownSource_Returns400()
    {
        using var factory = new FoundryWebUIFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/logs/eventlog");
        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SystemPrompts_Crud_RoundTrip()
    {
        using var factory = new FoundryWebUIFactory();
        using var client = factory.CreateClient();

        // List initial prompts.
        var initial = await client.GetFromJsonAsync<JsonElement>("/api/system-prompts");
        var initialCount = initial.GetArrayLength();

        // Create.
        var createResp = await client.PostAsJsonAsync("/api/system-prompts",
            new { name = "Reviewer", content = "You review code." });
        await Assert.That(createResp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        // List grew by one.
        var listAfter = await client.GetFromJsonAsync<JsonElement>("/api/system-prompts");
        await Assert.That(listAfter.GetArrayLength()).IsEqualTo(initialCount + 1);

        // Update.
        var updateResp = await client.PutAsJsonAsync($"/api/system-prompts/{id}",
            new { name = "Reviewer 2", content = "Updated." });
        await Assert.That(updateResp.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Delete.
        var deleteResp = await client.DeleteAsync($"/api/system-prompts/{id}");
        await Assert.That(deleteResp.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task IndexPage_ReturnsHtml()
    {
        using var factory = new FoundryWebUIFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/");
        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var html = await resp.Content.ReadAsStringAsync();
        await Assert.That(html).Contains("FoundryWebUI-X");
    }

    [Test]
    public async Task ModelsPage_ReturnsHtml()
    {
        using var factory = new FoundryWebUIFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/Models");
        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LogsPage_HasNoEventLogOrIisTabs()
    {
        using var factory = new FoundryWebUIFactory();
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/Logs");
        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var html = await resp.Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("data-source=\"eventlog\"");
        await Assert.That(html).DoesNotContain("data-source=\"iis\"");
        await Assert.That(html).DoesNotContain("data-source=\"app\"");
        await Assert.That(html).DoesNotContain("data-source=\"stdout\"");
        await Assert.That(html).Contains("data-source=\"foundry\"");
    }
}
