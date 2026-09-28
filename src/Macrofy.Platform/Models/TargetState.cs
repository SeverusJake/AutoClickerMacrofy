namespace Macrofy.Platform.Models;

// Coverage is a user-observed compatibility state; it is not inferred from WS_VISIBLE.
public enum TargetState { BackgroundVisible, BackgroundPartlyCovered, BackgroundCovered, Minimized }
public enum InputCapability { Click, Key, Shortcut, Text, Wheel, Drag, Hold }
