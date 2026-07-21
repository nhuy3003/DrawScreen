using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenDraw
{
    public class HotkeySettings
    {
        public string DesktopHotkey { get; set; } = "Alt+1";
        public string PencilHotkey { get; set; } = "Alt+2";
        public string RectangleHotkey { get; set; } = "Alt+3";
        public string UndoHotkey { get; set; } = "Ctrl+Shift+Z";

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScreenDraw",
                "hotkeys.txt");

        public static HotkeySettings Load()
        {
            var settings = new HotkeySettings();
            if (!File.Exists(SettingsPath))
                return settings;

            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length != 2)
                    continue;

                switch (parts[0].Trim())
                {
                    case nameof(DesktopHotkey):
                        settings.DesktopHotkey = parts[1].Trim();
                        break;
                    case nameof(PencilHotkey):
                        settings.PencilHotkey = parts[1].Trim();
                        break;
                    case nameof(RectangleHotkey):
                        settings.RectangleHotkey = parts[1].Trim();
                        break;
                    case nameof(UndoHotkey):
                        settings.UndoHotkey = parts[1].Trim();
                        break;
                }
            }

            return settings;
        }

        public void Save()
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllLines(SettingsPath, new[]
            {
                $"{nameof(DesktopHotkey)}={DesktopHotkey}",
                $"{nameof(PencilHotkey)}={PencilHotkey}",
                $"{nameof(RectangleHotkey)}={RectangleHotkey}",
                $"{nameof(UndoHotkey)}={UndoHotkey}"
            });
        }

        public static string Format(ModifierKeys modifiers, Key key)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");

            var keyName = key == Key.System ? string.Empty : key.ToString();
            if (!string.IsNullOrEmpty(keyName))
                parts.Add(keyName);

            return string.Join("+", parts);
        }

        public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var segments = text.Split('+').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
            if (segments.Count == 0)
                return false;

            for (int i = 0; i < segments.Count - 1; i++)
            {
                switch (segments[i].ToUpperInvariant())
                {
                    case "ALT":
                        modifiers |= 0x0001;
                        break;
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= 0x0002;
                        break;
                    case "SHIFT":
                        modifiers |= 0x0004;
                        break;
                    default:
                        return false;
                }
            }

            var keyToken = segments[segments.Count - 1];
            if (IsModifierName(keyToken))
                return false;

            if (keyToken.Length == 1 && char.IsDigit(keyToken[0]))
            {
                virtualKey = (uint)(Key.D0 + (keyToken[0] - '0'));
                virtualKey = (uint)KeyInterop.VirtualKeyFromKey((Key)virtualKey);
                return true;
            }

            if (Enum.TryParse(keyToken, true, out Key key))
            {
                virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                return virtualKey != 0;
            }

            return false;
        }

        private static bool IsModifierName(string value)
        {
            switch (value.ToUpperInvariant())
            {
                case "ALT":
                case "CTRL":
                case "CONTROL":
                case "SHIFT":
                    return true;
                default:
                    return false;
            }
        }
    }
}
