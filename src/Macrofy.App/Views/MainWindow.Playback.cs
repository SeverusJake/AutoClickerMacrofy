using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;

namespace Macrofy.App.Views;
public sealed partial class MainWindow
{
    private readonly IWorkspacePlaybackController playback;
    private readonly IGlobalHotkeys hotkeys;
    private readonly CompatibilityUiServices? compatibility;
    private readonly IAsyncDisposable? ownedServices;
    private bool closing, closeAllowed;
    private volatile bool capturingShortcut;
    private HotkeySet? registeredKeys;
    private string? registrationError;

    private void PlaybackChanged(object? sender, EventArgs e)
    {
        if (!playback.HotkeysReady) CancelCompatibilityTest();
        Dispatcher.UIThread.Post(() => { if (!closeAllowed) { if (!playback.HotkeysReady) DiscardCompatibility(); RefreshPlayback(); } });
    }
    private void RefreshPlayback()
    {
        footerStatus.Text = Workspace.AggregateStatus;
        pauseAll.IsEnabled = Workspace.ActiveCount > 0;
        stopAll.IsEnabled = Workspace.ActiveCount > 0 || CompatibilityLocked;
        runAllButton.IsEnabled = Workspace.Profile.Macros.Any(CanRunAll);
        var paused = Workspace.ActiveCount > 0 && Workspace.Sessions.Values.Where(s => s.IsActive).All(s => s.State == PlaybackState.Paused);
        SetIcon(pauseAll, paused ? "play" : "pause", $"{(paused ? "Resume" : "Pause")} all ({Workspace.Document.Shortcuts.Pause})", "warning");
        SetIcon(stopAll, "stop", $"Stop all ({Workspace.Document.Shortcuts.Stop})", "danger");
        ToolTip.SetTip(runAllButton, playback.HotkeysReady ? $"Run enabled macros ({Workspace.Document.Shortcuts.Run}); Screen starts after 3 seconds" : playback.HotkeyError?.Message ?? registrationError ?? "Global Stop unavailable");
        if (!dirty && (registrationError is not null || !playback.HotkeysReady))
            messageText.Text = registrationError ?? playback.HotkeyError?.Message ?? "Global emergency Stop unavailable.";
        if (commonProfile is not null) commonProfile.IsEnabled = !CompatibilityLocked;
        foreach (var refresh in refreshPlayback) refresh();
    }
    private bool CanRun(Macro macro, out string reason, bool selectedAction = false)
    {
        if (closing || CompatibilityLocked) { reason = "Finish or discard the compatibility test first."; return false; }
        if (HasDraft(macro)) { reason = "Apply or discard action edits first."; return false; }
        var owner = Workspace.Owner(macro);
        if (owner is null) { reason = "Macro profile is missing."; return false; }
        return playback.CanStart(owner, macro, out reason, selectedAction, selectedAction ? selectedStep : -1);
    }
    private bool CanRunAll(Macro macro) => macro.Enabled && CanRun(macro, out _);
    private async void Start(Macro macro) => await StartMacroAsync(macro);
    private async Task StartMacroAsync(Macro macro, bool selectedAction = false)
    {
        if (!CanRun(macro, out var reason, selectedAction)) { if (!dirty) messageText.Text = reason; return; }
        var owner = Workspace.Owner(macro)!;
        var index = selectedStep;
        var started = playback.StartAsync(owner, macro, selectedAction, index);
        RefreshPlayback();
        try
        {
            var result = await started;
            if (result.Queued) AddLog($"{(selectedAction ? "Action test" : "Playback")}: {owner.Name} / {macro.Name}");
            else if (!dirty) messageText.Text = result.Error?.Message ?? "Playback could not start.";
        }
        catch (Exception error) { if (!dirty) messageText.Text = "Playback failed: " + error.Message; }
        RefreshPlayback();
    }
    private void RunAllEnabled()
    {
        var skipped = 0;
        foreach (var macro in Workspace.Profile.Macros.Where(m => m.Enabled))
        {
            if (CanRunAll(macro)) Start(macro);
            else { skipped++; AddLog("Skipped: " + macro.Name); }
        }
        if (!dirty && store?.LoadError is null && skipped > 0) messageText.Text = $"Skipped {skipped}: check edits, steps, targets or active runs.";
        RefreshPlayback();
    }
    private void TogglePauseAll() { playback.TogglePauseAll(); RefreshPlayback(); }
    private void StopAll() { playback.StopAll(); CancelCompatibilityTest(); DiscardCompatibility(); RefreshPlayback(); }
    private void Suspend()
    {
        playback.StopAll(); CancelCompatibilityTest();
        Dispatcher.UIThread.Post(() => { DiscardCompatibility(); RefreshPlayback(); });
    }
    private void GlobalCommand(HotkeyCommand command)
    {
        // Native callbacks must cancel before any dispatcher work; capture consumes registered keys.
        if (capturingShortcut)
        {
            var keys = registeredKeys;
            var key = command switch { HotkeyCommand.Run => keys?.Run.Key, HotkeyCommand.Pause => keys?.Pause.Key, _ => keys?.Stop.Key };
            Dispatcher.UIThread.Post(() => { if (key is not null && shortcutCapture is not null) CaptureShortcut(key); });
            return;
        }
        if (command == HotkeyCommand.Stop) { playback.StopAll(); CancelCompatibilityTest(); }
        Dispatcher.UIThread.Post(() =>
        {
            if (closing) return;
            if (command == HotkeyCommand.Run) RunAllEnabled();
            else if (command == HotkeyCommand.Pause) TogglePauseAll();
            else { DiscardCompatibility(); RefreshPlayback(); }
        });
    }
    private bool ConfigureHotkeys(ShortcutSettings candidate)
    {
        if (Workspace.ActiveCount > 0 || CompatibilityLocked) { registrationError = "Stop input before changing global shortcuts."; return false; }
        var set = new HotkeySet(new(candidate.Run), new(candidate.Pause), new(candidate.Stop));
        var result = hotkeys.Configure(set);
        registrationError = result.Error?.Message;
        if (result.Registered) registeredKeys = set;
        return result.Registered;
    }
    private void HandleShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.Key.ToString();
        if (!key.StartsWith('F') || !int.TryParse(key.AsSpan(1), out var number) || number is < 1 or > 12) return;
        if (shortcutCapture is null) return; // Global service alone dispatches playback commands.
        e.Handled = true;
        if (registeredKeys is { } keys && (keys.Run.Key == key || keys.Pause.Key == key || keys.Stop.Key == key)) return;
        CaptureShortcut(key);
    }
    private void CaptureShortcut(string key)
    {
        if (shortcutCapture is null) return;
        var action = shortcutCapture.Tag as string;
        var current = Workspace.Document.Shortcuts;
        var candidate = new ShortcutSettings { Run = current.Run, Pause = current.Pause, Stop = current.Stop };
        if (action == "Run") candidate.Run = key; else if (action == "Pause") candidate.Pause = key; else candidate.Stop = key;
        if (new[] { candidate.Run, candidate.Pause, candidate.Stop }.Distinct().Count() != 3)
        { messageText.Text = $"{key} is already assigned to another control."; return; }
        if (!ConfigureHotkeys(candidate)) { messageText.Text = registrationError; return; }
        Workspace.Document.Shortcuts = candidate;
        shortcutCapture.Text = key; Save(); RefreshPlayback();
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closeAllowed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; timer.Stop(); playback.StopAll(); CancelCompatibilityTest(); DiscardCompatibility();
        try
        {
            await compatibilityWork;
            await playback.DisposeAsync();
            if (ownedServices is not null) await ownedServices.DisposeAsync();
        }
        finally
        {
            playback.Changed -= PlaybackChanged; hotkeys.Triggered -= GlobalCommand;
            if (hotkeys is ISystemEvents systemEvents) systemEvents.Suspended -= Suspend;
            closeAllowed = true; Close();
        }
    }
}
