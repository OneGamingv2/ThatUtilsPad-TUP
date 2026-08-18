using UnityEngine;

namespace TUPshaders.GUI
{
    /// <summary>
    /// ThatUtilsPad purple IMGUI theme for the TUPshaders desktop window.
    /// </summary>
    public static class GuiTheme
    {
        public static readonly Color Bg = new(0.10f, 0.08f, 0.14f, 1f);
        public static readonly Color Panel = new(0.14f, 0.11f, 0.20f, 1f);
        public static readonly Color Sidebar = new(0.08f, 0.07f, 0.12f, 1f);
        public static readonly Color Accent = new(0.72f, 0.55f, 0.98f, 1f);
        public static readonly Color AccentHover = new(0.82f, 0.68f, 1f, 1f);
        public static readonly Color AccentDark = new(0.42f, 0.28f, 0.62f, 1f);
        public static readonly Color Text = new(0.94f, 0.92f, 0.98f, 1f);
        public static readonly Color TextDim = new(0.72f, 0.68f, 0.80f, 1f);
        public static readonly Color Danger = new(0.88f, 0.32f, 0.42f, 1f);
        public static readonly Color Success = new(0.40f, 0.78f, 0.58f, 1f);
        public static readonly Color SliderBg = new(0.20f, 0.16f, 0.28f, 1f);
        public static readonly Color HeaderBar = new(0.16f, 0.12f, 0.24f, 1f);

        private static GUIStyle _window;
        private static GUIStyle _header;
        private static GUIStyle _subHeader;
        private static GUIStyle _label;
        private static GUIStyle _button;
        private static GUIStyle _buttonAccent;
        private static GUIStyle _toggle;
        private static GUIStyle _search;
        private static GUIStyle _sidebarBtn;
        private static GUIStyle _sidebarBtnActive;
        private static GUIStyle _section;
        private static bool _ready;
        private static Texture2D _texBg;
        private static Texture2D _texPanel;
        private static Texture2D _texAccent;
        private static Texture2D _texAccentHover;
        private static Texture2D _texAccentDark;
        private static Texture2D _texSidebar;
        private static Texture2D _texSlider;
        private static Texture2D _texHeader;

        public static void Ensure()
        {
            if (_ready && _window != null)
                return;

            _texBg = MakeTex(Bg);
            _texPanel = MakeTex(Panel);
            _texAccent = MakeTex(Accent);
            _texAccentHover = MakeTex(AccentHover);
            _texAccentDark = MakeTex(AccentDark);
            _texSidebar = MakeTex(Sidebar);
            _texSlider = MakeTex(SliderBg);
            _texHeader = MakeTex(HeaderBar);

            _window = new GUIStyle(UnityEngine.GUI.skin.window)
            {
                normal = { background = _texBg, textColor = Text },
                onNormal = { background = _texBg, textColor = Text },
                hover = { background = _texBg, textColor = Text },
                onHover = { background = _texBg, textColor = Text },
                active = { background = _texBg, textColor = Text },
                onActive = { background = _texBg, textColor = Text },
                focused = { background = _texBg, textColor = Text },
                onFocused = { background = _texBg, textColor = Text },
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(12, 12, 8, 12),
                fontSize = 14
            };

            _header = new GUIStyle(UnityEngine.GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Text },
                alignment = TextAnchor.MiddleLeft
            };

            _subHeader = new GUIStyle(UnityEngine.GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = TextDim },
                alignment = TextAnchor.MiddleLeft
            };

            _label = new GUIStyle(UnityEngine.GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Text },
                richText = true
            };

            _section = new GUIStyle(UnityEngine.GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Accent }
            };

            _button = new GUIStyle(UnityEngine.GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { background = _texPanel, textColor = Text },
                hover = { background = _texAccentDark, textColor = Text },
                active = { background = _texAccent, textColor = Color.black },
                padding = new RectOffset(12, 12, 8, 8),
                border = new RectOffset(4, 4, 4, 4)
            };

            _buttonAccent = new GUIStyle(_button)
            {
                normal = { background = _texAccent, textColor = new Color(0.08f, 0.06f, 0.12f) },
                hover = { background = _texAccentHover, textColor = Color.black },
                active = { background = _texAccentDark, textColor = Text }
            };

            _toggle = new GUIStyle(UnityEngine.GUI.skin.toggle)
            {
                fontSize = 13,
                normal = { textColor = Text },
                onNormal = { textColor = Accent },
                hover = { textColor = AccentHover },
                onHover = { textColor = AccentHover }
            };

            _search = new GUIStyle(UnityEngine.GUI.skin.textField)
            {
                fontSize = 13,
                normal = { background = _texPanel, textColor = Text },
                focused = { background = _texPanel, textColor = Text },
                padding = new RectOffset(10, 10, 7, 7)
            };

            _sidebarBtn = new GUIStyle(_button)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { background = _texSidebar, textColor = TextDim },
                hover = { background = _texPanel, textColor = Text }
            };

            _sidebarBtnActive = new GUIStyle(_sidebarBtn)
            {
                normal = { background = _texAccentDark, textColor = Text },
                hover = { background = _texAccent, textColor = Color.black },
                fontStyle = FontStyle.Bold
            };

            _ready = true;
        }

        public static void Reset()
        {
            _ready = false;
            _window = null;
        }

        public static GUIStyle Window => _window;
        public static GUIStyle Header => _header;
        public static GUIStyle SubHeader => _subHeader;
        public static GUIStyle Label => _label;
        public static GUIStyle Section => _section;
        public static GUIStyle Button => _button;
        public static GUIStyle ButtonAccent => _buttonAccent;
        public static GUIStyle Toggle => _toggle;
        public static GUIStyle Search => _search;
        public static GUIStyle SidebarBtn => _sidebarBtn;
        public static GUIStyle SidebarBtnActive => _sidebarBtnActive;
        public static Texture2D SliderTex => _texSlider;
        public static Texture2D HeaderTex => _texHeader;
        public static Texture2D BgTex => _texBg;

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(new[] { c, c, c, c });
            t.Apply();
            return t;
        }
    }
}
