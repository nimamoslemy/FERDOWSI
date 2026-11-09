using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace GrammerV
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct GvMonitorState
    {
        public int caret_x;
        public int caret_y;
        public int caret_w;
        public int caret_h;
        public int caret_source;
        public int mouse_x;
        public int mouse_y;
        public int is_ibeam_cursor;
        public int has_selection_event;
        public int is_typing_active;
        public int typed_char_count;
        public ulong last_type_tick;
        public ulong last_select_tick;
        public long foreground_hwnd;
        public long focus_hwnd;
        public int is_password_focused;
    }

    public class UiaInspectionResult
    {
        public bool IsPassword;
        public bool IsEditable;
        public bool HasCaretRect;
        public int CaretX;
        public int CaretY;
        public int CaretW;
        public int CaretH;
        public string SelectedText;
        public string ControlValueText;
        public AutomationElement Element;
        public bool SupportsValueSet;

        public UiaInspectionResult()
        {
            SelectedText = string.Empty;
            ControlValueText = string.Empty;
        }
    }

    public static class NativeBridge
    {
        private const string DllName = "grammerv_native.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetDllDirectoryW(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        // Native C++ DLL exports
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int gv_init();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gv_shutdown();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gv_set_own_windows(long overlay_hwnd, long studio_hwnd);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gv_set_password_mode(int is_password);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int gv_poll_state(out GvMonitorState out_state);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int gv_get_typed_buffer([Out] StringBuilder out_buf, int max_chars);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gv_clear_typed_buffer();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gv_ack_selection_event();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int gv_capture_selection(long target_hwnd, [Out] StringBuilder out_buf, int max_chars);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int gv_capture_full_field(long target_hwnd, [Out] StringBuilder out_buf, int max_chars);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int gv_replace_text(long target_hwnd, string new_text, int replace_mode, int backspace_count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int gv_insert_text_at_cursor(long target_hwnd, string text);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int gv_voice_start_recording();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int gv_voice_stop_recording(string out_wav_path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int gv_voice_is_recording();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int gv_damerau_distance(string a, string b);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int gv_spell_check_word(string word, [Out] StringBuilder out_suggestion, int max_chars);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int gv_phi_silica_status();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int gv_phi_silica_rewrite(string text, int tone_mode, string custom_instruction,
            [Out] StringBuilder out_buf, int max_chars, out int out_elapsed_ms);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int WM_HOTKEY = 0x0312;

        public const int HOTKEY_ID_ALT_G = 9001;
        public const int HOTKEY_ID_CTRL_SHIFT_G = 9002;
        public const int HOTKEY_ID_ALT_V = 9003;
        public const int HOTKEY_ID_CTRL_SHIFT_V = 9004;
        public const int HOTKEY_ID_ALT_R = 9005;
        public const int HOTKEY_ID_CTRL_SHIFT_R = 9006;
        public const int HOTKEY_ID_ALT_Q = 9007;
        public const int HOTKEY_ID_CTRL_SHIFT_F = 9008;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint VK_G = 0x47;
        public const uint VK_V = 0x56;
        public const uint VK_R = 0x52;
        public const uint VK_Q = 0x51;
        public const uint VK_F = 0x46;

        public static bool IsNativeLoaded { get; private set; }
        public static string LoadedDllPath { get; private set; }

        public static void EnablePerMonitorDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
                SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFileW(string lpFileName);

        public static void UnblockFileZoneIdentifier(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                DeleteFileW(filePath + ":Zone.Identifier");
            }
            catch { }
        }

        public static bool InitializeNativeDll(string runtimeDir)
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string candidate1 = Path.Combine(exeDir, DllName);
                string candidate2 = Path.Combine(runtimeDir, DllName);

                string chosen = File.Exists(candidate1) ? candidate1 : candidate2;
                if (File.Exists(chosen))
                {
                    UnblockFileZoneIdentifier(chosen);
                    SetDllDirectoryW(Path.GetDirectoryName(chosen));
                    IntPtr h = LoadLibraryW(chosen);
                    if (h != IntPtr.Zero)
                    {
                        LoadedDllPath = chosen;
                        IsNativeLoaded = (gv_init() == 1);
                        return IsNativeLoaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Native DLL load error: " + ex.Message);
            }
            IsNativeLoaded = false;
            return false;
        }

        public static void MakeWindowNonActivating(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
        }

        public static void SetWindowActivable(IntPtr hwnd, bool activable)
        {
            if (hwnd == IntPtr.Zero) return;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (activable)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_NOACTIVATE) | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
                try { SetForegroundWindow(hwnd); } catch { }
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
            }
        }

        public static void MoveWindowPhysicalNoActivate(IntPtr hwnd, int physX, int physY)
        {
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, physX, physY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static RECT GetWorkAreaForPhysicalPoint(int x, int y)
        {
            POINT pt;
            pt.X = x;
            pt.Y = y;
            IntPtr hMon = MonitorFromPoint(pt, 2); // MONITOR_DEFAULTTONEAREST
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (hMon != IntPtr.Zero && GetMonitorInfo(hMon, ref mi))
            {
                return mi.rcWork;
            }
            RECT fallback;
            fallback.Left = 0;
            fallback.Top = 0;
            fallback.Right = 1920;
            fallback.Bottom = 1040;
            return fallback;
        }

        public static string GetTypedBufferText()
        {
            if (!IsNativeLoaded) return string.Empty;
            StringBuilder sb = new StringBuilder(1024);
            int len = gv_get_typed_buffer(sb, sb.Capacity);
            return len > 0 ? sb.ToString(0, len) : string.Empty;
        }

        public static string CaptureSelectionFromTarget(long targetHwnd)
        {
            if (!IsNativeLoaded) return string.Empty;
            StringBuilder sb = new StringBuilder(8192);
            int len = gv_capture_selection(targetHwnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString(0, len) : string.Empty;
        }

        public static string CaptureFullFieldFromTarget(long targetHwnd)
        {
            if (!IsNativeLoaded) return string.Empty;
            StringBuilder sb = new StringBuilder(8192);
            int len = gv_capture_full_field(targetHwnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString(0, len) : string.Empty;
        }

        public static string NativeSpellSuggest(string word)
        {
            if (!IsNativeLoaded || string.IsNullOrEmpty(word)) return null;
            StringBuilder sb = new StringBuilder(64);
            int ok = gv_spell_check_word(word, sb, sb.Capacity);
            return ok == 1 ? sb.ToString() : null;
        }

        public static string GetWindowClass(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            StringBuilder sb = new StringBuilder(128);
            int len = GetClassNameW(hwnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString() : string.Empty;
        }

        public static UiaInspectionResult InspectFocusedUiaElement(IntPtr overlayHwnd)
        {
            UiaInspectionResult res = new UiaInspectionResult();
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null) return res;

                int nativeHandle = 0;
                try { nativeHandle = focused.Current.NativeWindowHandle; } catch { }
                if (nativeHandle != 0 && new IntPtr(nativeHandle) == overlayHwnd)
                {
                    return res;
                }

                res.Element = focused;

                // Privacy & Security Guard: Detect password fields across Browsers (Edge/Chrome/Firefox), Win32, and WPF
                bool isPwd = false;
                try { isPwd = focused.Current.IsPassword; } catch { }
                if (!isPwd)
                {
                    try
                    {
                        string cls = focused.Current.ClassName ?? string.Empty;
                        if (string.Equals(cls, "PasswordBox", StringComparison.OrdinalIgnoreCase))
                        {
                            isPwd = true;
                        }
                    }
                    catch { }
                }

                if (isPwd)
                {
                    res.IsPassword = true;
                    res.IsEditable = false;
                    if (IsNativeLoaded)
                    {
                        gv_set_password_mode(1);
                        gv_clear_typed_buffer();
                    }
                    return res;
                }

                if (IsNativeLoaded)
                {
                    gv_set_password_mode(0);
                }

                ControlType ct = null;
                try { ct = focused.Current.ControlType; } catch { }

                bool isExplicitlyReadOnly = false;
                object valObj;
                if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out valObj) && valObj is ValuePattern)
                {
                    ValuePattern vp = (ValuePattern)valObj;
                    try
                    {
                        if (!vp.Current.IsReadOnly)
                        {
                            res.IsEditable = true;
                            res.SupportsValueSet = true;
                            res.ControlValueText = vp.Current.Value ?? string.Empty;
                        }
                        else
                        {
                            isExplicitlyReadOnly = true;
                        }
                    }
                    catch { }
                }

                if (!isExplicitlyReadOnly && ct == ControlType.Edit)
                {
                    res.IsEditable = true;
                }

                System.Windows.Rect elemBounds = System.Windows.Rect.Empty;
                try { elemBounds = focused.Current.BoundingRectangle; } catch { }
                bool isLargeViewport = !elemBounds.IsEmpty && elemBounds.Width > 500 && elemBounds.Height > 220;

                object textObj;
                if (focused.TryGetCurrentPattern(TextPattern.Pattern, out textObj) && textObj is TextPattern)
                {
                    TextPattern tp = (TextPattern)textObj;
                    try
                    {
                        TextPatternRange[] sel = tp.GetSelection();
                        if (sel != null && sel.Length > 0 && sel[0] != null)
                        {
                            string selTxt = sel[0].GetText(2000);
                            if (!string.IsNullOrEmpty(selTxt))
                            {
                                res.SelectedText = selTxt;
                            }

                            if (res.IsEditable || !string.IsNullOrEmpty(res.SelectedText))
                            {
                                System.Windows.Rect[] rects = sel[0].GetBoundingRectangles();
                                if (rects != null && rects.Length > 0)
                                {
                                    System.Windows.Rect r = rects[0];
                                    bool isBogusViewportOrigin = isLargeViewport &&
                                                                 string.IsNullOrEmpty(res.SelectedText) &&
                                                                 Math.Abs(r.X - elemBounds.X) <= 16 &&
                                                                 Math.Abs(r.Y - elemBounds.Y) <= 16;
                                    if (!isBogusViewportOrigin && !r.IsEmpty && (r.X != 0 || r.Y != 0) &&
                                        r.Height >= 0 && r.Height <= 160 && r.Width <= 800)
                                    {
                                        res.HasCaretRect = true;
                                        res.CaretX = (int)Math.Round(r.X);
                                        res.CaretY = (int)Math.Round(r.Y);
                                        res.CaretW = Math.Max(2, (int)Math.Round(r.Width));
                                        res.CaretH = Math.Max(16, (int)Math.Round(r.Height));
                                    }
                                }
                            }
                        }

                        if (res.IsEditable && string.IsNullOrEmpty(res.SelectedText) && string.IsNullOrEmpty(res.ControlValueText))
                        {
                            string docTxt = tp.DocumentRange.GetText(4000);
                            if (!string.IsNullOrEmpty(docTxt))
                            {
                                res.ControlValueText = docTxt.TrimEnd('\r', '\n');
                            }
                        }
                    }
                    catch { }
                }

                // Fallback bounding rect of compact single-line edit box if caret rect was hidden
                if (res.IsEditable && !res.HasCaretRect)
                {
                    try
                    {
                        if (!elemBounds.IsEmpty && elemBounds.Width > 10 && elemBounds.Width <= 650 &&
                            elemBounds.Height > 8 && elemBounds.Height <= 90 && (elemBounds.X != 0 || elemBounds.Y != 0))
                        {
                            res.HasCaretRect = true;
                            res.CaretX = (int)Math.Round(elemBounds.X + 18);
                            res.CaretY = (int)Math.Round(elemBounds.Y + 4);
                            res.CaretW = 2;
                            res.CaretH = Math.Max(18, (int)Math.Round(elemBounds.Height));
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                // Focused UIA element may vanish mid-query; ignore safely
            }
            return res;
        }
    }
}
