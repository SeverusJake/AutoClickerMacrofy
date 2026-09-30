using System.Runtime.InteropServices;
namespace Macrofy.Platform.Windows.Interop;
internal interface IHotkeyNative : IDisposable
{
    void Initialize();
    bool Register(int id,uint modifiers,uint key,out int error);
    void Unregister(int id);
    void Pump(Action<uint,nuint> callback);
}
internal sealed class HotkeyNative : IHotkeyNative
{
    delegate nint WindowProc(nint window,uint message,nuint wParam,nint lParam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct WindowClass
    {
        public uint Style; public WindowProc Procedure; public int ClassExtra,WindowExtra; public nint Instance,Icon,Cursor,Background;
        public string? Menu; public string Name;
    }
    readonly string className = "MacrofyHotkeys_" + Guid.NewGuid().ToString("N");
    WindowProc? procedure;
    Action<uint,nuint>? callback;
    nint window,instance;
    ushort atom;
    public void Initialize()
    {
        instance = GetModuleHandle(null); procedure = WndProc;
        var cls = new WindowClass { Procedure=procedure,Instance=instance,Name=className };
        atom = RegisterClass(ref cls); if(atom == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        // Parent zero creates a hidden top-level window; message-only windows do not receive power broadcasts.
        window = CreateWindowEx(0,className,"",0,0,0,0,0,0,0,instance,0);
        if(window == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    nint WndProc(nint hwnd,uint message,nuint wParam,nint lParam)
    {
        if(message is 0x312 or 0x218) { callback?.Invoke(message,wParam); if(message == 0x218) return 1; }
        return DefWindowProc(hwnd,message,wParam,lParam);
    }
    public bool Register(int id,uint modifiers,uint key,out int error)
    { var result=RegisterHotKey(window,id,modifiers,key); error=result ? 0 : Marshal.GetLastWin32Error(); return result; }
    public void Unregister(int id) => UnregisterHotKey(window,id);
    public void Pump(Action<uint,nuint> callback)
    {
        this.callback=callback;
        while(WindowNative.PeekMessage(out var message,0,0,0,1))
        { WindowNative.TranslateMessage(ref message); WindowNative.DispatchMessage(ref message); }
    }
    public void Dispose()
    {
        if(window != 0) { DestroyWindow(window); window=0; }
        if(atom != 0) { UnregisterClass(className,instance); atom=0; }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll",EntryPoint="RegisterClassW",CharSet=CharSet.Unicode,SetLastError=true)] static extern ushort RegisterClass(ref WindowClass cls);
    [DllImport("user32.dll",EntryPoint="CreateWindowExW",CharSet=CharSet.Unicode,SetLastError=true)] static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll",EntryPoint="DefWindowProcW")] static extern nint DefWindowProc(nint window,uint message,nuint wParam,nint lParam);
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(nint window,int id,uint modifiers,uint key);
    [DllImport("user32.dll",SetLastError=true)] static extern bool UnregisterHotKey(nint window,int id);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll",EntryPoint="UnregisterClassW",CharSet=CharSet.Unicode)] static extern bool UnregisterClass(string name,nint instance);
}
