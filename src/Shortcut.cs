using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace Snapline
{
    sealed class ShortcutSpec
    {
        internal readonly uint Modifiers, Key;
        internal ShortcutSpec(uint modifiers, uint key) { Modifiers = modifiers; Key = key; }
        internal static ShortcutSpec From(Preferences settings)
        {
            if (settings.CaptureShortcut == "None") return null;
            if (settings.CaptureShortcut == "Custom") return new ShortcutSpec(settings.CustomCaptureModifiers, settings.CustomCaptureKey);
            if (settings.CaptureShortcut == "CtrlPrintScreen") return new ShortcutSpec(2, 0x2C);
            if (settings.CaptureShortcut == "ShiftPrintScreen") return new ShortcutSpec(4, 0x2C);
            return new ShortcutSpec(2, 0x42);
        }
        internal static string Validate(uint modifiers, uint key)
        {
            if ((modifiers & 8) != 0) return "Win shortcuts belong to Windows. Use Ctrl, Shift or Alt.";
            if (modifiers == 0 || (modifiers & ~7u) != 0) return "Add Ctrl, Shift or Alt so normal typing is not affected.";
            if (key == 0x7B) return "F12 is reserved by the system. Choose another key.";
            if (modifiers == 1 && key == 0x73) return "Alt + F4 closes windows. Choose another.";
            bool supported = (key >= 0x41 && key <= 0x5A) || (key >= 0x30 && key <= 0x39) ||
                (key >= 0x60 && key <= 0x6F) || (key >= 0x70 && key <= 0x87) || key == 0x2C ||
                (key >= 0xBA && key <= 0xC0) || (key >= 0xDB && key <= 0xDF) || key == 0xE2;
            return supported ? null : "Choose a letter, digit, symbol, function key or PrtSc.";
        }
        internal bool SameKeys(ShortcutSpec other) { return other != null && other.Modifiers == Modifiers && other.Key == Key; }
        // Another of Snapline's own shortcuts that already uses these keys, as a message; null when they are free.
        // `except` is the line shortcut being changed (null when it is the capture shortcut, which is then not compared).
        internal static string Conflict(Preferences settings, ShortcutSpec spec, LineShortcut except)
        {
            if (spec == null) return null;
            if (except != null && spec.SameKeys(From(settings))) return spec.Label + " already captures a region. Choose another.";
            foreach (var each in LineShortcut.All)
                if (each != except && spec.SameKeys(each.Of(settings))) return spec.Label + " already " + each.Does + ". Choose another.";
            return null;
        }
        internal static string ModifierLabel(uint modifiers)
        {
            var parts = new List<string>();
            if ((modifiers & 2) != 0) parts.Add("Ctrl");
            if ((modifiers & 1) != 0) parts.Add("Alt");
            if ((modifiers & 4) != 0) parts.Add("Shift");
            if ((modifiers & 8) != 0) parts.Add("Win");
            return String.Join(" + ", parts);
        }
        internal string Label
        {
            get
            {
                string name;
                switch (Key)
                {
                    case 0x2C: name = "PrtSc"; break;
                    case 0xBA: name = ";"; break; case 0xBB: name = "="; break;
                    case 0xBC: name = ","; break; case 0xBD: name = "-"; break;
                    case 0xBE: name = "."; break; case 0xBF: name = "/"; break;
                    case 0xC0: name = "`"; break; case 0xDB: name = "["; break;
                    case 0xDC: name = "\\"; break; case 0xDD: name = "]"; break;
                    case 0xDE: name = "'"; break;
                    default:
                        name = Key >= 0x30 && Key <= 0x39 ? ((char)Key).ToString() : ((Forms.Keys)Key).ToString();
                        break;
                }
                return ModifierLabel(Modifiers) + " + " + name;
            }
        }
        internal string Warning
        {
            get
            {
                if (Modifiers == 2 && "ABCFNOPRSTUVWXYZ".Contains((char)Key))
                    return "Apps commonly use this shortcut; once enabled, capture takes priority.";
                if (Modifiers == 6 && "BCIJNTV".Contains((char)Key))
                    return "Some apps use this shortcut; once enabled, capture takes priority.";
                return "Only captures made with this shortcut are collected; other screenshots are not.";
            }
        }
        internal static bool IsModifier(Key key)
        {
            return key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
                key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
                key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
                key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin;
        }
        internal static uint ModifiersFrom(ModifierKeys modifiers)
        {
            return ((modifiers & ModifierKeys.Control) != 0 ? 2u : 0u) |
                ((modifiers & ModifierKeys.Alt) != 0 ? 1u : 0u) |
                ((modifiers & ModifierKeys.Shift) != 0 ? 4u : 0u) |
                ((modifiers & ModifierKeys.Windows) != 0 ? 8u : 0u);
        }
    }

    // The shortcuts that open the line, hang the clipboard and start a note. Each has its own hotkey id on the shelf window
    // and can be the original keys, keys of your own, or off.
    sealed class LineShortcut
    {
        internal readonly string Name, Does, Title;
        internal readonly int Id;
        readonly uint defaultKey;
        readonly Func<Preferences, string> read;
        readonly Action<Preferences, string> write;
        LineShortcut(string name, string title, string does, int id, uint key, Func<Preferences, string> get, Action<Preferences, string> set)
        { Name = name; Title = title; Does = does; Id = id; defaultKey = key; read = get; write = set; }
        internal static readonly LineShortcut Open = new LineShortcut("Open", "Open the line", "opens the line", 1, 0x53, p => p.OpenShortcut, (p, v) => p.OpenShortcut = v);
        internal static readonly LineShortcut Hang = new LineShortcut("Hang", "Hang the clipboard", "hangs the clipboard", 4, 0x44, p => p.HangShortcut, (p, v) => p.HangShortcut = v);
        internal static readonly LineShortcut Note = new LineShortcut("Note", "Start a new note", "starts a new note", 5, 0x4E, p => p.NoteShortcut, (p, v) => p.NoteShortcut = v);
        internal static readonly LineShortcut[] All = { Open, Hang, Note };
        internal ShortcutSpec Default { get { return new ShortcutSpec(3, defaultKey); } }
        internal string Raw(Preferences settings) { return read(settings) ?? ""; }
        internal void Write(Preferences settings, string raw) { write(settings, raw); }
        internal bool IsOff(Preferences settings) { return Raw(settings) == "off"; }
        internal bool IsDefault(Preferences settings) { var spec = Of(settings); return spec != null && spec.SameKeys(Default); }
        // The keys in use, or null when off. Anything unreadable or no longer allowed reads as the original keys.
        internal ShortcutSpec Of(Preferences settings)
        {
            string raw = Raw(settings).Trim();
            if (raw == "off") return null;
            var parts = raw.Split(',');
            uint modifiers, key;
            if (parts.Length == 2 && UInt32.TryParse(parts[0], out modifiers) && UInt32.TryParse(parts[1], out key) && ShortcutSpec.Validate(modifiers, key) == null)
                return new ShortcutSpec(modifiers, key);
            return Default;
        }
        internal string Encode(ShortcutSpec spec)
        {
            if (spec == null) return "off";
            return spec.SameKeys(Default) ? "" : spec.Modifiers + "," + spec.Key;
        }
        internal string Label(Preferences settings) { var spec = Of(settings); return spec == null ? "Off" : spec.Label; }
    }

    sealed partial class Shelf
    {
        int captureHotkeyId = 2;
        uint captureTriggerKey;
        bool editingShortcut;
        internal bool ShortcutCollects { get { return store.Settings.CaptureShortcut == "CtrlB" || store.Settings.CaptureShortcut == "Custom"; } }
        internal int CaptureHotkeyId { get { return captureHotkeyId; } }

        internal bool TrySelectCaptureShortcut(string mode, uint modifiers, uint key, out string error)
        {
            error = null;
            if (mode != "CtrlB" && mode != "CtrlPrintScreen" && mode != "ShiftPrintScreen" && mode != "None" && mode != "Custom")
            { error = "Unknown shortcut option."; return false; }
            if (mode == "Custom" && (error = ShortcutSpec.Validate(modifiers, key)) != null) return false;
            if (mode != "None")
            {
                var wanted = mode == "Custom" ? new ShortcutSpec(modifiers, key) : mode == "CtrlB" ? new ShortcutSpec(2, 0x42) : new ShortcutSpec(mode == "ShiftPrintScreen" ? 4u : 2u, 0x2C);
                if ((error = ShortcutSpec.Conflict(store.Settings, wanted, null)) != null) return false;
            }
            string oldMode = store.Settings.CaptureShortcut;
            uint oldModifiers = store.Settings.CustomCaptureModifiers, oldKey = store.Settings.CustomCaptureKey;
            var oldSpec = ShortcutSpec.From(store.Settings);
            var next = mode == "None" ? null : mode == "Custom" ? new ShortcutSpec(modifiers, key) :
                mode == "CtrlB" ? new ShortcutSpec(2, 0x42) : new ShortcutSpec(mode == "ShiftPrintScreen" ? 4u : 2u, 0x2C);
            bool sameRegistration = CaptureHotkeyRegistered && oldSpec != null && next != null &&
                oldSpec.Modifiers == next.Modifiers && oldSpec.Key == next.Key;
            int nextId = captureHotkeyId == 2 ? 3 : 2;
            bool staged = false;
            if (next != null && !sameRegistration)
            {
                if (!Native.RegisterHotKey(handle, nextId, next.Modifiers | 0x4000u, next.Key))
                { error = next.Label + " could not be enabled; another program may be using it. Choose another."; return false; }
                staged = true;
            }
            store.Settings.CaptureShortcut = mode;
            if (mode == "Custom") { store.Settings.CustomCaptureModifiers = modifiers; store.Settings.CustomCaptureKey = key; }
            try { store.SaveSettings(); }
            catch (Exception ex)
            {
                store.Settings.CaptureShortcut = oldMode;
                store.Settings.CustomCaptureModifiers = oldModifiers; store.Settings.CustomCaptureKey = oldKey;
                if (staged) Native.UnregisterHotKey(handle, nextId);
                store.Log("Save capture shortcut", ex);
                error = "Could not save the setting; the previous shortcut is kept. Try again."; return false;
            }
            if (!sameRegistration)
            {
                Native.UnregisterHotKey(handle, captureHotkeyId);
                if (staged) captureHotkeyId = nextId;
                CaptureHotkeyRegistered = staged;
            }
            UpdateShortcutMenu(); Render();
            return true;
        }
        internal void EditCaptureShortcut()
        {
            if (dialogOpen || capturePending || shuttingDown) return;
            dialogOpen = true; editingShortcut = true;
            CloseImagePreview(); CloseNotePreview();
            if (helpTip != null) helpTip.IsOpen = false;
            // Let this application's current hotkey reach the recorder instead of starting capture.
            Native.UnregisterHotKey(handle, captureHotkeyId); CaptureHotkeyRegistered = false;
            try
            {
                ShortcutSpec candidate = ShortcutSpec.Validate(store.Settings.CustomCaptureModifiers, store.Settings.CustomCaptureKey) == null ?
                    new ShortcutSpec(store.Settings.CustomCaptureModifiers, store.Settings.CustomCaptureKey) : ShortcutSpec.From(store.Settings);
                var editor = new ShortcutEditor(candidate, delegate(ShortcutSpec choice)
                {
                    string error;
                    return TrySelectCaptureShortcut("Custom", choice.Modifiers, choice.Key, out error) ? null : error;
                });
                editor.Owner = this;
                editor.ShowDialog();
            }
            finally
            {
                editingShortcut = false; dialogOpen = false;
                if (!CaptureHotkeyRegistered) RegisterCaptureShortcut();
                UpdateShortcutMenu(); Render();
                keepUntil = DateTime.Now.AddSeconds(3);
            }
        }

        // ---- open, hang and note shortcuts ----

        internal bool LineShortcutRegistered(LineShortcut which)
        {
            return which == LineShortcut.Open ? HotkeyRegistered : which == LineShortcut.Hang ? HangHotkeyRegistered : NoteHotkeyRegistered;
        }
        void SetLineShortcutRegistered(LineShortcut which, bool on)
        {
            if (which == LineShortcut.Open) HotkeyRegistered = on; else if (which == LineShortcut.Hang) HangHotkeyRegistered = on; else NoteHotkeyRegistered = on;
        }
        bool RegisterLineShortcut(LineShortcut which)
        {
            Native.UnregisterHotKey(handle, which.Id);
            var spec = which.Of(store.Settings);
            bool on = spec != null && Native.RegisterHotKey(handle, which.Id, spec.Modifiers | 0x4000u, spec.Key); // No repeat.
            SetLineShortcutRegistered(which, on);
            return on;
        }
        void RegisterLineShortcuts() { foreach (var each in LineShortcut.All) RegisterLineShortcut(each); }

        // Sets one of the line shortcuts to new keys, or off with null. Keeps the old keys when the new ones are taken or cannot be saved.
        internal bool TrySetLineShortcut(LineShortcut which, ShortcutSpec next, out string error)
        {
            error = null;
            if (next != null && ((error = ShortcutSpec.Validate(next.Modifiers, next.Key)) != null || (error = ShortcutSpec.Conflict(store.Settings, next, which)) != null)) return false;
            string oldRaw = which.Raw(store.Settings);
            var old = which.Of(store.Settings);
            bool same = next != null && next.SameKeys(old) && LineShortcutRegistered(which);
            if (!same)
            {
                Native.UnregisterHotKey(handle, which.Id);
                if (next != null && !Native.RegisterHotKey(handle, which.Id, next.Modifiers | 0x4000u, next.Key))
                {
                    RegisterLineShortcut(which); // The old keys again.
                    error = next.Label + " could not be enabled; another program may be using it. Choose another.";
                    return false;
                }
                SetLineShortcutRegistered(which, next != null);
            }
            which.Write(store.Settings, which.Encode(next));
            try { store.SaveSettings(); }
            catch (Exception ex)
            {
                which.Write(store.Settings, oldRaw); RegisterLineShortcut(which);
                store.Log("Save " + which.Name + " shortcut", ex);
                error = "Could not save the setting; the previous shortcut is kept. Try again."; return false;
            }
            UpdateShortcutMenu(); Render();
            return true;
        }
        // Records new keys for one of the line shortcuts in the same dialog as the capture shortcut.
        internal void EditLineShortcut(LineShortcut which)
        {
            if (dialogOpen || capturePending || shuttingDown) return;
            dialogOpen = true; editingShortcut = true;
            CloseImagePreview(); CloseNotePreview();
            if (helpTip != null) helpTip.IsOpen = false;
            // Let the current keys reach the recorder instead of doing what they do.
            Native.UnregisterHotKey(handle, which.Id); SetLineShortcutRegistered(which, false);
            try
            {
                var editor = new ShortcutEditor(which.Of(store.Settings) ?? which.Default, delegate(ShortcutSpec choice)
                {
                    string error;
                    return TrySetLineShortcut(which, choice, out error) ? null : error;
                }, which.Title);
                editor.Owner = this;
                editor.ShowDialog();
            }
            finally
            {
                editingShortcut = false; dialogOpen = false;
                if (!LineShortcutRegistered(which)) RegisterLineShortcut(which);
                UpdateShortcutMenu(); Render();
                keepUntil = DateTime.Now.AddSeconds(3);
            }
        }
        internal string LineShortcutLabel(LineShortcut which) { return which.Label(store.Settings); }
    }
}
