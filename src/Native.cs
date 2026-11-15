// Snapline: a local screenshot shelf for Windows. MIT License.
// Windows API declarations.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Path = System.IO.Path;

namespace Snapline
{
    static class Native
    {
        internal const int Hotkey = 0x0312, MouseActivate = 0x0021;
        internal static readonly uint ShowMessage = RegisterWindowMessage("Snapline.Windows.Show.v1");


        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr h, StringBuilder name, int count);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out Rect r);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point p);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] internal static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Rect r, int size);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        internal static bool CaptureKeysReleased(uint triggerKey)
        {
            return new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.All(key => (GetAsyncKeyState(key) & 0x8000) == 0) &&
                (triggerKey == 0 || (GetAsyncKeyState((int)triggerKey) & 0x8000) == 0);
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
    }
}
