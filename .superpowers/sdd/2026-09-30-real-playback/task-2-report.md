# Task 2 report: screen delivery

Status: complete for native-seam delivery scope. No real screen injection performed.

## Contracts

- `ScreenGeometry(int Left, int Top, int Width, int Height)` is a readonly record struct in Platform.Models. Geometry is physical virtual-desktop pixels.
- `ScreenPointResult(PointerPoint? Point, PlatformError? Error = null)` is in Platform.Models.
- `IScreenInputPlayer.ReadGeometry()`, `ReadPointer()`, `SendAsync(InputCommand, CancellationToken = default)`, `ReleaseHeldAsync(CancellationToken = default)`.
- Additive `DeliveryResult(bool Queued, PlatformError? Error = null, PlatformError? CleanupError = null)` preserves existing constructor calls and Queued/Error members. Task 6 should map Error and CleanupError independently.
- Public `WindowsScreenInputPlayer()` composes the native adapter. Internal `IScreenPlayerNative` seam provides physical geometry/pointer, monitor membership, required key/button state and SendInput insertion count.
- Existing `WindowsScreenClicker` remains a separate one-batch move/down/up compatibility API with existing error behavior. Failed release additionally supplies CleanupError.

## Native behavior

Full sequential INPUT plus explicit MOUSEINPUT/KEYBDINPUT/HARDWAREINPUT union: 40 bytes on this x64 runner (28 on x86), union offset 8 on x64. Keyboard uses virtual-key input, optional native scan-code flag, extended-key flags and key-up flags. Unicode preserves each validated UTF-16 surrogate unit in down/up pairs. Wheel reads and validates the current physical pointer, emits signed 32-bit mouseData and never moves the pointer.

Pointer coordinates validate finite values, virtual-desktop bounds and connected monitor membership. Negative monitor coordinates normalize against desktop origin; first/last pixels map to 0/65535. Pointer Down and Up each encode ordered move then button event. Higher-level compiler owns percentage-to-pixel conversion and complete click/chord gesture order.

Ownership is acquired only for inserted downs and removed only for inserted ups. Physical required input is rejected before down injection, and unowned Up cannot release it. Cleanup emits remaining releases in reverse acquisition order. Partial insertion/cancellation preserve delivery errors while independent cleanup attempts release only acquired inputs. Failed cleanup retains the remaining ownership for retry. Delivery cancellation never cancels its cleanup token. Cleanup has a 500ms token budget.

## TDD and verification evidence

1. Tests first, command:
   `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~ScreenInputPlayerTests`
   Initial missing-feature run failed with CS0246 for IScreenPlayerNative/ScreenNativeInput/ScreenGeometry. API-only failure stubs enabled executable RED: 15 failed, 2 passed, 17 total, no skips. Expected failures included Queued false and Error `NotImplemented`; native size and unowned-Up rejection already held.
   Initial Unicode string InlineData produced duplicate IDs; replaced with integer surrogate units before the recorded executable RED. No duplicate IDs remain.
2. Implemented generalized encoder/native union/ownership. Command:
   `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter "FullyQualifiedName~ScreenInputPlayerTests|FullyQualifiedName~ScreenClickerTests"`
   GREEN: 23 passed, zero failed/skipped.
3. Added compatibility cleanup regression and partial pointer coverage. Regression RED command:
   `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~ClickerCleanupFailureAddsIndependentCleanupError`
   One failure: Assert.NotNull failure because CleanupError was null. Added independent CleanupError without changing legacy Error. Same combined screen/clicker command GREEN: 26 passed, zero failed/skipped.
4. Full project suite:
   `dotnet test Macrofy.sln -c Release`
   Exit 0. Core 32 passed; Windows 60 passed; App 31 passed: 123 total, zero failed/skipped. Build output had no compiler warnings/errors. ScreenInputPlayerTests use only injected native fakes; legacy suite uses existing controlled window receiver tests.

## Sources and concerns

Primary Microsoft references checked:
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-input
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput

SendInput failure does not prove UIPI caused the rejection; errors say input may be blocked by permissions. Native SendInput is synchronous and cannot be forcibly interrupted by a CancellationToken; 500ms cleanup budget bounds managed scheduling/checks, not an OS call stalled inside user32. GetAsyncKeyState prevents acquisition of input already held at validation but does not eliminate a physical-input race between validation and insertion. No low-level hooks are introduced. Complete gestures must remain externally serialized and followed by ReleaseHeldAsync before releasing the dispatch gate. No real screen or game-compatibility claim is made; controlled real screen delivery remains later verification scope.
