using System.ComponentModel;
using System.Runtime.InteropServices;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Microsoft.Win32.SafeHandles;

namespace Macrofy.Platform.Windows;

internal interface IProcessAccess { int? GetIntegrityLevel(int processId); }

public sealed class WindowsPermissionService : IPermissionService
{
    private readonly WindowIdentityRegistry registry;
    private readonly IProcessAccess access;
    public WindowsPermissionService(WindowsWindowCatalog catalog) : this(catalog.Registry, new ProcessAccess()) { }
    internal WindowsPermissionService(WindowIdentityRegistry registry, IProcessAccess access) { this.registry = registry; this.access = access; }
    public Task<PermissionResult> CheckAsync(TargetToken target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!registry.TryResolve(target, out var window)) return Task.FromResult(new PermissionResult(false, new("TargetLost", "Original target disappeared.")));
        var ours = access.GetIntegrityLevel(Environment.ProcessId);
        var theirs = access.GetIntegrityLevel(window.ProcessId);
        return Task.FromResult(ours is not null && theirs is not null && ours >= theirs
            ? new PermissionResult(true)
            : new PermissionResult(false, new("PermissionDenied", "Target integrity is higher or access cannot be checked. Run both applications at the same normal privilege level.")));
    }
}

internal sealed class ProcessAccess : IProcessAccess
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int infoClass, nint data, uint length, out uint needed);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthority(nint sid, uint index);
    public int? GetIntegrityLevel(int processId)
    {
        using var process = OpenProcess(0x1000, false, processId);
        if (process.IsInvalid || !OpenProcessToken(process, 8, out var token)) return null;
        using (token)
        {
            GetTokenInformation(token, 25, 0, 0, out var needed);
            if (needed == 0) return null;
            var data = Marshal.AllocHGlobal(checked((int)needed));
            try
            {
                if (!GetTokenInformation(token, 25, data, needed, out _)) return null;
                var sid = Marshal.ReadIntPtr(data);
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return count == 0 ? null : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally { Marshal.FreeHGlobal(data); }
        }
    }
}
