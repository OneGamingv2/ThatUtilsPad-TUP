using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace TUPshaders.Utilities
{
    /// <summary>
    /// Keyboard polling via Win32 GetAsyncKeyState.
    /// Avoids UnityEngine.Input, which throws when the game uses the Input System package only.
    /// </summary>
    public static class RawKeyboard
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static readonly HashSet<int> _held = new();
        private static readonly Dictionary<KeyCode, int> _map = BuildMap();

        public static bool GetKey(KeyCode key)
        {
            if (!_map.TryGetValue(key, out int vk)) return false;
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        public static bool GetKeyDown(KeyCode key)
        {
            if (!_map.TryGetValue(key, out int vk)) return false;
            bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
            if (down)
            {
                if (_held.Contains(vk)) return false;
                _held.Add(vk);
                return true;
            }
            _held.Remove(vk);
            return false;
        }

        /// <summary>
        /// First newly pressed mapped key this tick, or KeyCode.None.
        /// </summary>
        public static KeyCode GetAnyKeyDown()
        {
            foreach (var kv in _map)
            {
                if (GetKeyDown(kv.Key))
                    return kv.Key;
            }
            return KeyCode.None;
        }

        public static void Tick()
        {
            // Drop released keys so GetKeyDown can fire again.
            if (_held.Count == 0) return;
            var remove = ListPool.Rent();
            foreach (var vk in _held)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) == 0)
                    remove.Add(vk);
            }
            foreach (var vk in remove) _held.Remove(vk);
            ListPool.Return(remove);
        }

        private static Dictionary<KeyCode, int> BuildMap()
        {
            var m = new Dictionary<KeyCode, int>(128);
            // Letters A-Z
            for (int i = 0; i < 26; i++)
                m[(KeyCode)((int)KeyCode.A + i)] = 0x41 + i;
            // Digits 0-9
            for (int i = 0; i < 10; i++)
                m[(KeyCode)((int)KeyCode.Alpha0 + i)] = 0x30 + i;
            // Numpad
            for (int i = 0; i < 10; i++)
                m[(KeyCode)((int)KeyCode.Keypad0 + i)] = 0x60 + i;

            m[KeyCode.Space] = 0x20;
            m[KeyCode.Return] = 0x0D;
            m[KeyCode.Escape] = 0x1B;
            m[KeyCode.Tab] = 0x09;
            m[KeyCode.Backspace] = 0x08;
            m[KeyCode.LeftShift] = 0xA0;
            m[KeyCode.RightShift] = 0xA1;
            m[KeyCode.LeftControl] = 0xA2;
            m[KeyCode.RightControl] = 0xA3;
            m[KeyCode.LeftAlt] = 0xA4;
            m[KeyCode.RightAlt] = 0xA5;
            m[KeyCode.UpArrow] = 0x26;
            m[KeyCode.DownArrow] = 0x28;
            m[KeyCode.LeftArrow] = 0x25;
            m[KeyCode.RightArrow] = 0x27;
            m[KeyCode.Insert] = 0x2D;
            m[KeyCode.Delete] = 0x2E;
            m[KeyCode.Home] = 0x24;
            m[KeyCode.End] = 0x23;
            m[KeyCode.PageUp] = 0x21;
            m[KeyCode.PageDown] = 0x22;
            m[KeyCode.F1] = 0x70;
            m[KeyCode.F2] = 0x71;
            m[KeyCode.F3] = 0x72;
            m[KeyCode.F4] = 0x73;
            m[KeyCode.F5] = 0x74;
            m[KeyCode.F6] = 0x75;
            m[KeyCode.F7] = 0x76;
            m[KeyCode.F8] = 0x77;
            m[KeyCode.F9] = 0x78;
            m[KeyCode.F10] = 0x79;
            m[KeyCode.F11] = 0x7A;
            m[KeyCode.F12] = 0x7B;
            m[KeyCode.Comma] = 0xBC;
            m[KeyCode.Period] = 0xBE;
            m[KeyCode.Slash] = 0xBF;
            m[KeyCode.Semicolon] = 0xBA;
            m[KeyCode.Quote] = 0xDE;
            m[KeyCode.LeftBracket] = 0xDB;
            m[KeyCode.RightBracket] = 0xDD;
            m[KeyCode.Backslash] = 0xDC;
            m[KeyCode.Minus] = 0xBD;
            m[KeyCode.Equals] = 0xBB;
            m[KeyCode.BackQuote] = 0xC0;
            return m;
        }

        private static class ListPool
        {
            [ThreadStatic] private static List<int>? _list;
            public static List<int> Rent()
            {
                var l = _list ?? new List<int>(8);
                _list = null;
                l.Clear();
                return l;
            }
            public static void Return(List<int> l)
            {
                l.Clear();
                _list = l;
            }
        }
    }
}
