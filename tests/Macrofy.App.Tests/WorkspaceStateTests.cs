using Macrofy.App.Models;
using Macrofy.App.Services;
using Xunit;

namespace Macrofy.App.Tests;

public class WorkspaceStateTests
{
    [Theory]
    [InlineData("{\"CompatibilityEvidence\":null}")]
    [InlineData("{\"CompatibilityEvidence\":[null]}")]
    [InlineData("{\"Profiles\":null}")]
    public void StructurallyInvalidDocumentsRemainPreserved(string invalidProperties)
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-malformed-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(folder);
            var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(WorkspaceDocument.CreateDefault()))!;
            foreach (var property in System.Text.Json.Nodes.JsonNode.Parse(invalidProperties)!.AsObject()) json[property.Key] = property.Value?.DeepClone();
            var path = Path.Combine(folder, "ui-workspace.json"); File.WriteAllText(path, json.ToJsonString());
            var original = File.ReadAllText(path); var store = new WorkspaceStore(folder); store.Load();
            Assert.NotNull(store.LoadError); Assert.Throws<InvalidOperationException>(() => store.Save(WorkspaceDocument.CreateDefault())); Assert.Equal(original, File.ReadAllText(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void LegacyUnsupportedActionSurvivesLoadAndCannotRun()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-legacy-" + Guid.NewGuid());
        try
        {
            var store = new WorkspaceStore(folder); var document = WorkspaceDocument.CreateDefault();
            document.Profiles[0].Macros[0].Steps = [new("Key", "LegacyUnsupported", 0)]; store.Save(document);
            var loaded = new WorkspaceStore(folder); var state = new WorkspaceState(loaded.Load());
            Assert.Null(loaded.LoadError); Assert.Equal("LegacyUnsupported", state.Macro.Steps[0].Value);
            Assert.False(WorkspaceState.ValidateStep(state.Macro.Steps[0], out _)); Assert.False(state.StartPreview(state.Macro));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void VersionOneWithoutEvidenceLoadsEmpty()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(WorkspaceDocument.CreateDefault()))!;
        json.AsObject().Remove("CompatibilityEvidence");
        var document = System.Text.Json.JsonSerializer.Deserialize<WorkspaceDocument>(json.ToJsonString())!;
        Assert.Empty(document.CompatibilityEvidence);
    }
    [Theory]
    [InlineData("Key", "Unknown")]
    [InlineData("Key", "Ctrl +")]
    [InlineData("Wheel", "0")]
    [InlineData("Wheel", "32768")]
    public void EditorUsesStrictSharedActionValidation(string kind, string value)
    {
        Assert.False(WorkspaceState.ValidateStep(new(kind, value, 0), out var error));
        Assert.NotEmpty(error);
    }
    [Fact]
    public void EditorCoordinatesDoNotAssumePercentageMode()
    {
        Assert.True(WorkspaceState.ValidateStep(new("Click", "-50, 1000", 0), out _));
    }
    [Fact]
    public void ExistingWorkspaceWithoutEnabledFlagDefaultsToEnabled()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(WorkspaceDocument.CreateDefault()))!;
        foreach (var profile in json["Profiles"]!.AsArray())
            foreach (var macro in profile!["Macros"]!.AsArray()) macro!.AsObject().Remove("Enabled");
        var loaded = System.Text.Json.JsonSerializer.Deserialize<WorkspaceDocument>(json.ToJsonString())!;
        Assert.All(loaded.Profiles.SelectMany(p => p.Macros), m => Assert.True(m.Enabled));
    }

    [Fact]
    public void MacroSelectionRestoresItsOwnStepsAndApp()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var profile = state.Profile;
        state.SelectMacro(profile.Macros[1].Id);
        Assert.Equal("Collect rewards", state.Macro.Name);
        Assert.Equal("CookieRun", state.TargetName(state.Macro));
        Assert.Equal("600, 420", state.Macro.Steps[0].Value);
        state.SelectMacro(profile.Macros[0].Id);
        Assert.Equal("480, 640", state.Macro.Steps[0].Value);
        state.Macro.AppId = null;
        Assert.Equal("Screen", state.TargetName(state.Macro));
        state.Macro.AppId = Guid.NewGuid();
        Assert.Equal("Missing app", state.TargetName(state.Macro));
        Assert.False(state.StartPreview(state.Macro));
    }

    [Fact]
    public void PreviewSessionsAreIndependentAndStopAllIncludesOtherProfiles()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var first = state.Profile.Macros[0];
        var second = state.Profile.Macros[1];
        second.AppId = first.AppId;
        Assert.True(state.StartPreview(first));
        Assert.True(state.StartPreview(second));
        Assert.False(state.StartPreview(first));
        state.TogglePause(first.Id);
        state.Tick();
        Assert.Equal("Paused", state.Status(first));
        Assert.Equal(0, state.Sessions[first.Id].CompletedSteps);
        Assert.Equal(1, state.Sessions[second.Id].CompletedSteps);
        Assert.False(state.ApplyStep(first, 0, new MacroStep("Click", "10, 20", 100), out _));
        state.SelectProfile(state.Document.Profiles[1].Id);
        Assert.True(state.StartPreview(state.Macro));
        state.StopAll();
        Assert.All(state.Sessions.Values, session => Assert.Equal("Stopped", session.State));
    }

    [Theory]
    [InlineData("Click", "bad", 100)]
    [InlineData("Wait", "-10", 100)]
    [InlineData("Key", "", 100)]
    [InlineData("Click", "10, 20", -1)]
    public void InvalidStepKeepsLastValidSequence(string kind, string value, int delay)
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var before = state.Macro.Steps[0];
        Assert.False(state.ApplyStep(state.Macro, 0, new MacroStep(kind, value, delay), out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, state.Macro.Steps[0]);
    }

    [Fact]
    public void ProfilesAppsAssignmentsAndAppearanceSurviveReopen()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-ui-tests-" + Guid.NewGuid());
        try
        {
            var store = new WorkspaceStore(folder);
            var document = WorkspaceDocument.CreateDefault();
            document.Theme = "synthwave"; document.Mode = "dark";
            document.Profiles[0].Apps.Add(new SavedApp { Name = "Another game", Executable = @"C:\Game\game.exe", TitleRule = "*Game*" });
            document.Profiles[0].Macros[0].AppId = document.Profiles[0].Apps[2].Id;
            document.Profiles[0].Macros[0].Steps.Add(new("Wait", "500", 0));
            store.Save(document);
            var loaded = store.Load();
            Assert.Equal("synthwave", loaded.Theme); Assert.Equal("dark", loaded.Mode);
            Assert.Equal(3, loaded.Profiles[0].Apps.Count);
            Assert.Equal(loaded.Profiles[0].Apps[2].Id, loaded.Profiles[0].Macros[0].AppId);
            Assert.Equal(2, loaded.Profiles[0].Macros[0].Steps.Count);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void CorruptOrNewerDataIsPreservedAndCannotBeOverwritten()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-ui-tests-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "ui-workspace.json");
            File.WriteAllText(path, "{broken");
            var store = new WorkspaceStore(folder);
            store.Load();
            Assert.Throws<InvalidOperationException>(() => store.Save(WorkspaceDocument.CreateDefault()));
            Assert.Equal("{broken", File.ReadAllText(path));
            File.WriteAllText(path, "{\"Version\":999,\"Profiles\":[]}");
            store = new WorkspaceStore(folder); store.Load();
            Assert.Throws<InvalidOperationException>(() => store.Save(WorkspaceDocument.CreateDefault()));
            Assert.Contains("999", File.ReadAllText(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void OnceWaitRemainsRunningUntilItsDurationEnds()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        state.Macro.Steps = [new("Wait", "5000", 0)]; state.Macro.Repeat = 1;
        Assert.True(state.StartPreview(state.Macro));
        state.Tick(600);
        Assert.Equal("Running", state.Status(state.Macro));
        Assert.Equal(0, state.Sessions[state.Macro.Id].CompletedSteps);
        state.Tick(4400);
        Assert.Equal("Stopped", state.Status(state.Macro));
        Assert.Equal(1, state.Sessions[state.Macro.Id].CompletedSteps);
    }

    [Fact]
    public void StaleInstanceCannotOverwriteAnotherInstancesEdits()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-ui-tests-" + Guid.NewGuid());
        try
        {
            var first = new WorkspaceStore(folder); var document = first.Load(); first.Save(document);
            var other = new WorkspaceStore(folder); var stale = other.Load();
            document.Profiles[0].Macros.Add(new Macro { Name = "Keep this macro" }); first.Save(document);
            stale.Mode = "dark";
            Assert.Throws<InvalidOperationException>(() => other.Save(stale));
            Assert.Contains(new WorkspaceStore(folder).Load().Profiles[0].Macros, m => m.Name == "Keep this macro");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
