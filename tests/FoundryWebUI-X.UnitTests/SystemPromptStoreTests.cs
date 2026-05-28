using FoundryWebUI.Services;

using Microsoft.Extensions.Logging.Abstractions;

namespace FoundryWebUI.UnitTests;

public class SystemPromptStoreTests
{
    private static SystemPromptStore CreateStore(out string filePath)
    {
        var dir = Path.Combine(Path.GetTempPath(), "fwx-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        filePath = Path.Combine(dir, "system-prompts.json");
        return new SystemPromptStore(filePath, NullLogger<SystemPromptStore>.Instance);
    }

    [Test]
    public async Task Constructor_ReturnsDefaultPromptWhenFileMissing()
    {
        var store = CreateStore(out _);
        var all = store.GetAll();

        await Assert.That(all.Count).IsEqualTo(1);
        await Assert.That(all[0].Name).IsEqualTo("Default");
        await Assert.That(all[0].IsDefault).IsTrue();
    }

    [Test]
    public async Task Add_AppendsPromptAndPersists()
    {
        var store = CreateStore(out var filePath);

        var added = store.Add("Code Reviewer", "You review code.");

        await Assert.That(added.Id).IsNotNull();
        await Assert.That(File.Exists(filePath)).IsTrue();
        await Assert.That(store.GetAll().Count).IsEqualTo(2);
    }

    [Test]
    public async Task Update_ChangesNameAndContent()
    {
        var store = CreateStore(out _);
        var added = store.Add("X", "original");

        var updated = store.Update(added.Id, "X", "new");

        await Assert.That(updated).IsNotNull();
        await Assert.That(updated!.Content).IsEqualTo("new");
    }

    [Test]
    public async Task Update_ReturnsNullForUnknownId()
    {
        var store = CreateStore(out _);
        var result = store.Update("missing", "X", "Y");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Delete_RemovesPrompt()
    {
        var store = CreateStore(out _);
        var added = store.Add("Tmp", "x");

        var success = store.Delete(added.Id);

        await Assert.That(success).IsTrue();
        await Assert.That(store.GetById(added.Id)).IsNull();
    }

    [Test]
    public async Task Delete_PromotesAnotherPromptToDefaultWhenDeletingDefault()
    {
        var store = CreateStore(out _);
        var other = store.Add("Other", "x");

        var defaultPrompt = store.GetDefault();
        var success = store.Delete(defaultPrompt!.Id);

        await Assert.That(success).IsTrue();
        var newDefault = store.GetDefault();
        await Assert.That(newDefault).IsNotNull();
        await Assert.That(newDefault!.Id).IsEqualTo(other.Id);
    }

    [Test]
    public async Task SetDefault_EnsuresExactlyOneDefault()
    {
        var store = CreateStore(out _);
        var p1 = store.Add("p1", "x");
        var p2 = store.Add("p2", "y");

        var success = store.SetDefault(p2.Id);

        await Assert.That(success).IsTrue();
        var defaults = store.GetAll().Where(p => p.IsDefault).ToList();
        await Assert.That(defaults.Count).IsEqualTo(1);
        await Assert.That(defaults[0].Id).IsEqualTo(p2.Id);
    }

    [Test]
    public async Task Load_PersistedFilesAreReadOnReinit()
    {
        var store1 = CreateStore(out var filePath);
        var added = store1.Add("Persisted", "p");

        var store2 = new SystemPromptStore(filePath, NullLogger<SystemPromptStore>.Instance);

        await Assert.That(store2.GetById(added.Id)).IsNotNull();
        await Assert.That(store2.GetById(added.Id)!.Name).IsEqualTo("Persisted");
    }
}
