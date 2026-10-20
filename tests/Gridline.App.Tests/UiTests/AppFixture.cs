using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Gridline.App.Tests.UiTests;

/// <summary>
/// Launches a fresh Gridline.App.exe (optionally with a file argument) with GRIDLINE_RECOVERY_DIR pointing at a fresh
/// temp dir, and kills it on dispose (§11.7).
/// </summary>
public sealed class AppFixture : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly Process _process;

    public string RecoveryDir { get; }
    public Application App { get; }
    public Window MainWindow { get; set; } = null!;
    public Stopwatch SinceStart { get; }

    public AppFixture(string? file = null, Action<string>? beforeLaunch = null, bool waitForMainWindow = true)
    {
        RecoveryDir = Path.Combine(Path.GetTempPath(), "gridline-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RecoveryDir);
        beforeLaunch?.Invoke(RecoveryDir);

        var psi = new ProcessStartInfo(ExePath()) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExePath())! };
        psi.Environment["GRIDLINE_RECOVERY_DIR"] = RecoveryDir;
        if (file is not null) psi.ArgumentList.Add(file);

        SinceStart = Stopwatch.StartNew();
        _process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Gridline.App.exe");
        try
        {
            App = Application.Attach(_process);
            if (waitForMainWindow) MainWindow = WaitForMainWindow();
        }
        catch
        {
            Cleanup(); // the constructor failed, so the caller's using never disposes this fixture
            throw;
        }
    }

    /// <summary>Gridline.App.exe is copied next to the test assembly by the project reference (Debug and Release).</summary>
    public static string ExePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Gridline.App.exe");
        if (!File.Exists(path)) throw new FileNotFoundException("Gridline.App.exe not found next to the test assembly", path);
        return path;
    }

    public Window WaitForMainWindow()
    {
        var w = Retry.WhileNull(() =>
        {
            try
            {
                _process.Refresh();
                if (_process.HasExited) throw new InvalidOperationException("Gridline.App exited during startup");
                var h = _process.MainWindowHandle;
                return h == IntPtr.Zero ? null : _automation.FromHandle(h).AsWindow();
            }
            catch (COMException) { return null; }
        }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(20)).Result;
        return w ?? throw new TimeoutException("Main window did not appear");
    }

    public AutomationElement Desktop => _automation.GetDesktop();

    public AutomationElement Find(string id)
    {
        var e = Retry.WhileNull(() => MainWindow.FindFirstDescendant(MainWindow.ConditionFactory.ByAutomationId(id)),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50)).Result;
        return e ?? throw new InvalidOperationException("No element with AutomationId " + id);
    }

    public string TextOf(string id)
    {
        var e = Find(id);
        if (e.Patterns.Value.IsSupported) return e.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
        return e.Name ?? "";
    }

    /// <summary>Polls until <paramref name="id"/>'s text satisfies the predicate; fails with the last text seen.</summary>
    public void WaitText(string id, Func<string, bool> ok, string what, int timeoutMs = 10000)
    {
        string last = "";
        var r = Retry.WhileFalse(() =>
        {
            try { last = TextOf(id); } catch (Exception) { return false; }
            return ok(last);
        }, TimeSpan.FromMilliseconds(timeoutMs), TimeSpan.FromMilliseconds(50));
        if (!r.Success) throw new Xunit.Sdk.XunitException($"{id}: expected {what}, last text was '{last}'");
    }

    public void WaitTitle(string expected)
    {
        string last = "";
        var r = Retry.WhileFalse(() => { last = MainWindow.Title; return last == expected; },
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50));
        if (!r.Success) throw new Xunit.Sdk.XunitException($"title: expected '{expected}', was '{last}'");
    }

    /// <summary>Gives the grid keyboard focus and verifies it got it.</summary>
    public AutomationElement FocusGrid()
    {
        var g = Find("Grid");
        var r = Retry.WhileFalse(() =>
        {
            MainWindow.SetForeground();
            g.Focus();
            return g.Properties.HasKeyboardFocus.ValueOrDefault;
        }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
        if (!r.Success) throw new Xunit.Sdk.XunitException("Grid did not receive keyboard focus");
        return g;
    }

    public static void PressEnter()
    {
        Keyboard.Press(VirtualKeyShort.RETURN);
        Keyboard.Release(VirtualKeyShort.RETURN);
    }

    /// <summary>Sets a text box through its Value pattern, then sends Enter to it (deterministic setup, no typing races).</summary>
    public void SubmitBox(string id, string text)
    {
        var box = Find(id);
        var r = Retry.WhileFalse(() =>
        {
            MainWindow.SetForeground();
            box.Focus();
            return box.Properties.HasKeyboardFocus.ValueOrDefault;
        }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
        if (!r.Success) throw new Xunit.Sdk.XunitException(id + " did not receive keyboard focus");
        box.Patterns.Value.Pattern.SetValue(text);
        PressEnter();
    }

    /// <summary>Navigates the active cell through the name box and waits until the name box reports it.</summary>
    public void GoTo(string address)
    {
        SubmitBox("NameBox", address);
        WaitText("NameBox", t => t == address, address);
        FocusGrid();
    }

    /// <summary>Puts <paramref name="input"/> into a cell via the formula bar and waits for its canonical value to show.</summary>
    public void SetCell(string address, string input, string expectedValueText)
    {
        GoTo(address);
        SubmitBox("FormulaBar", input);
        WaitText("SelectionValue", t => t == expectedValueText, expectedValueText);
        FocusGrid();
    }

    public void Dispose() => Cleanup();

    private void Cleanup()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); _process.WaitForExit(5000); } catch (Exception) { }
        try { _automation.Dispose(); } catch (Exception) { }
        try { _process.Dispose(); } catch (Exception) { }
        try { Directory.Delete(RecoveryDir, recursive: true); } catch (Exception) { }
    }
}

/// <summary>SendInput helpers: FlaUI sends arrow keys without KEYEVENTF_EXTENDEDKEY, which WPF ignores for selection.</summary>
internal static class ExtKeys
{
    private const uint KeyUp = 0x0002, Extended = 0x0001, InputKeyboard = 1;
    private const ushort VkShift = 0x10, VkDown = 0x28, VkUp = 0x26;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput { public ushort vk; public ushort scan; public uint flags; public uint time; public IntPtr extra; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion { [FieldOffset(0)] public KeybdInput ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint n, Input[] inputs, int size);

    private static Input Key(ushort vk, bool up, bool extended) => new()
    {
        type = InputKeyboard,
        u = new InputUnion { ki = new KeybdInput { vk = vk, flags = (up ? KeyUp : 0) | (extended ? Extended : 0) } },
    };

    private static void Send(params Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
    }

    public static void ShiftDown(int times) => ShiftArrow(VkDown, times);

    private static void ShiftArrow(ushort vk, int times)
    {
        Send(Key(VkShift, false, false));
        try
        {
            for (int i = 0; i < times; i++) Send(Key(vk, false, true), Key(vk, true, true));
        }
        finally { Send(Key(VkShift, true, false)); }
    }

    public static void ArrowUp() => Send(Key(VkUp, false, true), Key(VkUp, true, true));
}
