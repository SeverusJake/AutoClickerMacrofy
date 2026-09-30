using System.Collections.Concurrent;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows;
using Macrofy.Platform.Windows.Interop;
using Xunit;

namespace Macrofy.Platform.Windows.Tests;
public class GlobalHotkeyTests
{
    static HotkeySet Defaults => new(new("F9"), new("F8"), new("F10"));
    [Fact] public void RegistersNoRepeatAndRoutesCommands()
    {
        var native = new FakeNative(); using var service = new WindowsGlobalHotkeys(native);
        Assert.True(service.Configure(Defaults).Registered);
        Assert.All(native.Keys.Values, k => Assert.Equal(0x4000u, k.Modifiers));
        var commands = new ConcurrentQueue<HotkeyCommand>(); service.Triggered += commands.Enqueue;
        foreach (var key in new uint[] { 120, 119, 121 }) native.Messages.Enqueue((0x312, (nuint)native.Keys.Single(x => x.Value.Key == key).Key));
        Assert.True(SpinWait.SpinUntil(() => commands.Count == 3, 1000));
        Assert.Equal(new[] { HotkeyCommand.Run, HotkeyCommand.Pause, HotkeyCommand.Stop }, commands.ToArray());
    }
    [Theory] [InlineData("F12")] [InlineData("F13")] [InlineData("A")] [InlineData("F8")]
    public void InvalidSetKeepsOldStop(string key)
    {
        var native = new FakeNative(); using var service = new WindowsGlobalHotkeys(native); Assert.True(service.Configure(Defaults).Registered);
        var result = service.Configure(Defaults with { Run = new(key) }); Assert.False(result.Registered);
        Assert.Contains(key, result.Error!.Message); Assert.Contains(native.Keys.Values, k => k.Key == 121);
        if (key == "F12") Assert.Contains("debugger", result.Error.Message);
    }
    [Fact] public void FailedAddedBindingRetainsEntireOldSet()
    {
        var native = new FakeNative(); using var service = new WindowsGlobalHotkeys(native); service.Configure(Defaults);
        native.FailKey = 114; var result = service.Configure(new(new("F6"), new("F8"), new("F3")));
        Assert.False(result.Registered); Assert.Contains("Stop", result.Error!.Message); Assert.Contains("F3", result.Error.Message);
        Assert.Equal(new uint[] {119,120,121}, native.Keys.Values.Select(k => k.Key).Order().ToArray());
    }
    [Fact] public void SwapsReuseRegistrationsAndDisposeReleasesAll()
    {
        var native = new FakeNative(); var service = new WindowsGlobalHotkeys(native); service.Configure(Defaults);
        native.FailKey = 120; Assert.True(service.Configure(new(new("F10"), new("F8"), new("F9"))).Registered);
        service.Dispose(); service.Dispose(); Assert.Empty(native.Keys); Assert.True(native.Closed);
    }
    [Fact] public void SuspendDeduplicatesAndSubscriberFailureIsIsolated()
    {
        var native = new FakeNative(); using var service = new WindowsGlobalHotkeys(native); var count = 0;
        service.Suspended += () => throw new Exception(); service.Suspended += () => Interlocked.Increment(ref count);
        native.Messages.Enqueue((0x218,4)); native.Messages.Enqueue((0x218,4));
        Assert.True(SpinWait.SpinUntil(() => count == 1,1000));
        native.Messages.Enqueue((0x218,18)); native.Messages.Enqueue((0x218,4));
        Assert.True(SpinWait.SpinUntil(() => count == 2,1000));
    }
    [Fact] public void StartupFailureAndDisposedConfigureReturnErrors()
    {
        using var service = new WindowsGlobalHotkeys(new FakeNative { StartupThrows = true });
        Assert.False(service.Configure(Defaults).Registered); service.Dispose(); Assert.False(service.Configure(Defaults).Registered);
    }
    [Fact] public void HotkeySubscriberCanReconfigureWithoutDeadlock()
    {
        var native = new FakeNative(); using var service = new WindowsGlobalHotkeys(native); service.Configure(Defaults);
        HotkeyRegistrationResult? result = null;
        service.Triggered += _ => result = service.Configure(Defaults);
        native.Messages.Enqueue((0x312,(nuint)native.Keys.Single(k => k.Value.Key == 121).Key));
        Assert.True(SpinWait.SpinUntil(() => result is not null,3000)); Assert.True(result!.Registered);
    }
    [Fact] public void ExternalConfigureDoesNotBlockReentrantNativeCallback()
    {
        var native = new FakeNative(); using var service = new WindowsGlobalHotkeys(native); service.Configure(Defaults);
        using var pumpEntered = new ManualResetEventSlim(); using var releasePump = new ManualResetEventSlim();
        HotkeyRegistrationResult? callbackResult = null, externalResult = null;
        service.Triggered += _ => callbackResult = service.Configure(Defaults);
        native.PumpHook = callback => { pumpEntered.Set(); releasePump.Wait(); callback(0x312,3); };
        Assert.True(pumpEntered.Wait(1000));
        var caller = new Thread(() => externalResult = service.Configure(Defaults)); caller.Start();
        Assert.True(SpinWait.SpinUntil(() => caller.ThreadState.HasFlag(ThreadState.WaitSleepJoin),1000));
        releasePump.Set(); Assert.True(caller.Join(4000));
        Assert.True(SpinWait.SpinUntil(() => callbackResult is not null,1000));
        Assert.True(externalResult!.Registered); Assert.True(callbackResult!.Registered);
    }
    [Fact] public void ConcurrentConfigureReturnsBoundedlyWhileNativeOwnerIsStalled()
    {
        var native = new FakeNative(); using var service = new WindowsGlobalHotkeys(native); service.Configure(Defaults);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        native.RegisterHook = key => { if(key == 117) { entered.Set(); release.Wait(); } };
        service.Triggered += _ => service.Configure(new(new("F6"),new("F8"),new("F10")));
        native.Messages.Enqueue((0x312,3)); Assert.True(entered.Wait(1000));
        HotkeyRegistrationResult? result = null;
        var caller = new Thread(() => result = service.Configure(Defaults)); caller.Start();
        var returnedBoundedly = caller.Join(3000);
        release.Set(); Assert.True(caller.Join(3000));
        Assert.True(returnedBoundedly); Assert.False(result!.Registered);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ShutdownDuringRegistrationRollsBackWithoutPumpingOrDispatch(bool dispose)
    {
        var native = new FakeNative(); using var service = new WindowsGlobalHotkeys(native); service.Configure(Defaults);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        native.RegisterHook = key => { if(key == 117) { entered.Set(); release.Wait(); } };
        var count = 0; service.Triggered += _ => Interlocked.Increment(ref count); service.Suspended += () => Interlocked.Increment(ref count);
        HotkeyRegistrationResult? result = null;
        var caller = new Thread(() => result = service.Configure(new(new("F6"),new("F5"),new("F4")))); caller.Start();
        Assert.True(entered.Wait(1000)); var pumpCount = native.PumpCount;
        native.Messages.Enqueue((0x312,4)); native.Messages.Enqueue((0x218,4));
        if(dispose) service.Dispose();
        Assert.True(caller.Join(3000)); Assert.False(result!.Registered);
        release.Set(); Assert.True(SpinWait.SpinUntil(() => native.Closed,1000));
        Assert.Equal(0,count); Assert.Equal(pumpCount,native.PumpCount);
        Assert.Equal(new uint[] {120,119,121,117},native.RegisteredKeys.ToArray()); Assert.Empty(native.Keys);
    }
    sealed class FakeNative : IHotkeyNative
    {
        public ConcurrentDictionary<int,(uint Modifiers,uint Key)> Keys = new();
        public ConcurrentQueue<(uint,nuint)> Messages = new(); public uint FailKey; public bool StartupThrows; public volatile bool Closed; public Action<uint>? RegisterHook; public Action<Action<uint,nuint>>? PumpHook; public int PumpCount; public ConcurrentQueue<uint> RegisteredKeys = new();
        public void Initialize() { if(StartupThrows) throw new InvalidOperationException("startup failed"); }
        public bool Register(int id,uint modifiers,uint key,out int error) { RegisterHook?.Invoke(key); RegisteredKeys.Enqueue(key); error=1409; if(key==FailKey) return false; return Keys.TryAdd(id,(modifiers,key)); }
        public void Unregister(int id) => Keys.TryRemove(id,out _);
        public void Pump(Action<uint,nuint> callback) { Interlocked.Increment(ref PumpCount); Interlocked.Exchange(ref PumpHook,null)?.Invoke(callback); while(Messages.TryDequeue(out var m)) callback(m.Item1,m.Item2); }
        public void Dispose() => Closed=true;
    }
}


