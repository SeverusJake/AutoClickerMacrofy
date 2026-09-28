namespace Macrofy.Platform.Models;

public abstract record InputCommand;
public sealed record PointerCommand(PointerKind Kind, PointerPoint Point, MouseButton? Button = null, int WheelDelta = 0) : InputCommand;
public sealed record KeyCommand(KeyKind Kind, KeyIdentity Key) : InputCommand;
public sealed record TextCommand(string Text) : InputCommand;
public enum PointerKind { Move, Down, Up, VerticalWheel, HorizontalWheel }
public enum KeyKind { Down, Up }
public enum MouseButton { Left, Right, Middle, X1, X2 }
