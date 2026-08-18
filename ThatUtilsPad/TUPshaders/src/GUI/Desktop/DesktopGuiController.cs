using System;
using System.IO;
using System.Text;
using TUPshaders.Core;
using TUPshaders.Performance;
using TUPshaders.Presets;
using TUPshaders.Rendering;
using TUPshaders.Settings;
using TUPshaders.Utilities;
using UnityEngine;

namespace TUPshaders.GUI.Desktop
{
    /// <summary>
    /// Stable TUP-purple desktop GUI. Fixed-size window (no shrink animation),
    /// clamped dimensions, expand-fill scroll content.
    /// </summary>
    public sealed class DesktopGuiController : MonoBehaviour
    {
        private const float MinW = 640f;
        private const float MinH = 480f;
        private const float MaxW = 1100f;
        private const float MaxH = 780f;
        private const float DefaultW = 820f;
        private const float DefaultH = 560f;

        private SettingsUiModel _model;
        private Rect _window = new(80, 80, DefaultW, DefaultH);
        private bool _visible;
        private float _anim;
        private Vector2 _scroll;
        private Vector2 _sidebarScroll;
        private bool _resizing;
        private KeyCode _toggleKey = KeyCode.F;
        private string _saveName = "MyPreset";
        private string _importBuffer = "";
        private bool _showImport;
        private bool _rebinding;
        private Texture2D _colorPreview;
        private readonly int _windowId = 0x54555053;
        private Rect _contentRect;

        public bool IsVisible => _visible;
        public SettingsUiModel Model => _model;

        public void Initialize(
            SettingsManager settings,
            PresetManager presets,
            GraphicsFramework framework,
            PerformanceManager performance)
        {
            _model = new SettingsUiModel(settings, presets, framework, performance);
            _model.SelectedPreset = settings.Get("preset.startup", "Realistic");
            _toggleKey = (KeyCode)settings.Get("gui.toggleKey", (int)KeyCode.F);

            float w = settings.Get("gui.w", DefaultW);
            float h = settings.Get("gui.h", DefaultH);
            // Recover from corrupted / animation-shrunk saves.
            if (w < MinW || h < MinH || w > MaxW || h > MaxH)
            {
                w = DefaultW;
                h = DefaultH;
            }

            float x = settings.Get("gui.x", 80f);
            float y = settings.Get("gui.y", 80f);
            _window = new Rect(x, y, w, h);
            ClampWindowToScreen();

            _colorPreview = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            GuiTheme.Reset();
        }

        public void RefreshFromSettings() { }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (visible)
            {
                ClampWindowToScreen();
                if (_window.width < MinW || _window.height < MinH)
                    _window = new Rect(_window.x, _window.y, DefaultW, DefaultH);
            }
            else
                PersistWindow();
        }

        private void Update()
        {
            if (_model == null) return;

            RawKeyboard.Tick();
            if (_rebinding)
            {
                var pressed = RawKeyboard.GetAnyKeyDown();
                if (pressed != KeyCode.None)
                    ApplyRebind(pressed);
            }
            else if (RawKeyboard.GetKeyDown(_toggleKey))
            {
                SetVisible(!_visible);
            }

            float target = _visible ? 1f : 0f;
            _anim = Mathf.MoveTowards(_anim, target, Time.unscaledDeltaTime * 8f);

            if (_model.StatusTimer > 0f)
                _model.StatusTimer -= Time.unscaledDeltaTime;
        }

        private void OnGUI()
        {
            if (_model == null || _anim <= 0.01f)
                return;

            GuiTheme.Ensure();
            ClampWindowToScreen();

            // Opaque draw only — translucent GUI.color left ghost trails of old text
            // on GT's IMGUI compositor. Snap visible; no fade.
            if (_anim < 0.5f)
                return;

            Color prev = UnityEngine.GUI.color;
            UnityEngine.GUI.color = Color.white;

            Rect drawn = GUILayout.Window(_windowId, _window, DrawWindow, GUIContent.none, GuiTheme.Window);
            if (drawn.width >= MinW * 0.9f && drawn.height >= MinH * 0.9f)
                _window = drawn;

            HandleResize();
            UnityEngine.GUI.color = prev;
        }

        private void DrawWindow(int id)
        {
            float winW = Mathf.Max(MinW, _window.width);
            float winH = Mathf.Max(MinH, _window.height);

            // Solid opaque fill every frame so nothing from last layout can trail.
            if (Event.current.type == EventType.Repaint)
            {
                if (GuiTheme.BgTex != null)
                    UnityEngine.GUI.DrawTexture(new Rect(0, 0, winW, winH), GuiTheme.BgTex);
                if (GuiTheme.HeaderTex != null)
                    UnityEngine.GUI.DrawTexture(new Rect(0, 0, winW, 52), GuiTheme.HeaderTex);
            }

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("  TUPshaders", GuiTheme.Header, GUILayout.Height(28));
            GUILayout.Label("  ThatUtilsPad graphics  ·  Press F to toggle", GuiTheme.SubHeader, GUILayout.Height(18));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GuiTheme.Button, GUILayout.Width(72), GUILayout.Height(32)))
                SetVisible(false);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Toolbar
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply", GuiTheme.ButtonAccent, GUILayout.Height(30), GUILayout.Width(88)))
            {
                // Re-apply the highlighted preset so look modes (IRL / neon) always refresh.
                if (!string.IsNullOrEmpty(_model.SelectedPreset))
                    _model.ApplyPreset(_model.SelectedPreset);
                else
                {
                    _model.Settings.Save();
                    _model.Framework.SyncFromSettings();
                    PcMonitorAutoFix.Request("Apply");
                }
                _model.SetStatus("Applied: " + _model.SelectedPreset);
            }
            if (GUILayout.Button("Save", GuiTheme.Button, GUILayout.Height(30), GUILayout.Width(72)))
            {
                _model.Settings.Save();
                PersistWindow();
                _model.SetStatus("Settings saved");
            }
            if (GUILayout.Button("Reset", GuiTheme.Button, GUILayout.Height(30), GUILayout.Width(72)))
                _model.ResetDefaults();
            if (GUILayout.Button("Export", GuiTheme.Button, GUILayout.Height(30), GUILayout.Width(72)))
                ExportConfig();
            if (GUILayout.Button("Import", GuiTheme.Button, GUILayout.Height(30), GUILayout.Width(72)))
                _showImport = !_showImport;
            GUILayout.FlexibleSpace();
            DrawPerfHud();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GuiTheme.Label, GUILayout.Width(52));
            _model.SearchQuery = GUILayout.TextField(_model.SearchQuery ?? "", GuiTheme.Search, GUILayout.MinWidth(220), GUILayout.Height(28));
            GUILayout.FlexibleSpace();
            if (_model.StatusTimer > 0f)
                GUILayout.Label(_model.StatusMessage, GuiTheme.Label);
            GUILayout.EndHorizontal();

            if (_model.Settings.Get("debug.showActiveEffects", true))
            {
                GUILayout.Space(4);
                var sb = new StringBuilder("Active: ");
                bool any = false;
                foreach (var e in _model.Framework.ActiveEffects())
                {
                    if (any) sb.Append(", ");
                    sb.Append(e.DisplayName);
                    any = true;
                }
                if (!any) sb.Append("none");
                GUILayout.Label(sb.ToString(), GuiTheme.SubHeader);
            }

            GUILayout.Space(8);

            float bodyH = Mathf.Max(280f, winH - 210f);
            GUILayout.BeginHorizontal(GUILayout.Height(bodyH));

            // Sidebar
            GUILayout.BeginVertical(GUILayout.Width(200), GUILayout.Height(bodyH));
            _sidebarScroll = GUILayout.BeginScrollView(_sidebarScroll, false, true, GUILayout.Width(200), GUILayout.Height(bodyH));
            foreach (var cat in SettingsManager.Categories)
            {
                var style = cat == _model.SelectedCategory ? GuiTheme.SidebarBtnActive : GuiTheme.SidebarBtn;
                if (GUILayout.Button("  " + cat, style, GUILayout.Height(34), GUILayout.ExpandWidth(true)))
                    _model.SelectedCategory = cat;
                GUILayout.Space(2);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(10);

            // Main panel
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(bodyH));
            GUILayout.Label(_model.SelectedCategory.ToUpperInvariant(), GuiTheme.Section);
            GUILayout.Space(4);

            float mainH = Mathf.Max(220f, bodyH - (_showImport ? 150f : 8f));
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUILayout.Height(mainH), GUILayout.ExpandWidth(true));
            DrawCategoryEntries();
            if (_model.SelectedCategory == "Graphics")
                DrawPresetPanel();
            if (_model.SelectedCategory == "Fog")
            {
                GUILayout.Space(8);
                GUILayout.Label("FOG COLOR", GuiTheme.Section);
                DrawColorPicker("fx.fog.color");
            }
            if (_model.SelectedCategory == "Debug")
                DrawKeybindEditor();
            GUILayout.EndScrollView();

            if (_showImport)
            {
                GUILayout.Space(6);
                GUILayout.Label("Paste JSON config:", GuiTheme.Label);
                _importBuffer = GUILayout.TextArea(_importBuffer, GUILayout.Height(70), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Import JSON", GuiTheme.ButtonAccent, GUILayout.Height(28), GUILayout.Width(140)))
                {
                    if (_model.Presets.TryImportJson(_importBuffer, out var cfg))
                    {
                        _model.Presets.Apply(cfg);
                        _model.SetStatus("Imported configuration");
                        _showImport = false;
                    }
                    else _model.SetStatus("Import failed");
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // Drag from top strip only (avoids fighting content / resize).
            UnityEngine.GUI.DragWindow(new Rect(0, 0, winW - 90, 48));
        }

        private void DrawKeybindEditor()
        {
            GUILayout.Space(8);
            GUILayout.Label("KEYBINDS", GuiTheme.Section);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Desktop GUI Toggle", GuiTheme.Label, GUILayout.Width(240));
            var key = (KeyCode)_model.Settings.Get("gui.toggleKey", (int)KeyCode.F);
            GUILayout.Label(key.ToString(), GuiTheme.Label, GUILayout.Width(100));
            if (GUILayout.Button(_rebinding ? "Press any key…" : "Rebind", GuiTheme.Button, GUILayout.Width(140)))
                _rebinding = true;
            GUILayout.EndHorizontal();
        }

        private void DrawColorPicker(string keyPrefix)
        {
            float r = _model.Settings.Get(keyPrefix + ".r", 0.55f);
            float g = _model.Settings.Get(keyPrefix + ".g", 0.65f);
            float b = _model.Settings.Get(keyPrefix + ".b", 0.75f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("R", GuiTheme.Label, GUILayout.Width(16));
            r = GUILayout.HorizontalSlider(r, 0f, 1f, GUILayout.Width(120));
            GUILayout.Label("G", GuiTheme.Label, GUILayout.Width(16));
            g = GUILayout.HorizontalSlider(g, 0f, 1f, GUILayout.Width(120));
            GUILayout.Label("B", GuiTheme.Label, GUILayout.Width(16));
            b = GUILayout.HorizontalSlider(b, 0f, 1f, GUILayout.Width(120));
            if (_colorPreview != null)
            {
                _colorPreview.SetPixel(0, 0, new Color(r, g, b));
                _colorPreview.Apply();
                var rect = GUILayoutUtility.GetRect(28, 28, GUILayout.Width(28));
                UnityEngine.GUI.DrawTexture(rect, _colorPreview);
            }
            _model.Settings.Set(keyPrefix + ".r", r);
            _model.Settings.Set(keyPrefix + ".g", g);
            _model.Settings.Set(keyPrefix + ".b", b);
            GUILayout.EndHorizontal();
        }

        private void DrawCategoryEntries()
        {
            foreach (var entry in _model.EntriesForCategory(_model.SelectedCategory))
            {
                GUILayout.BeginHorizontal(GUILayout.Height(28));
                GUILayout.Label(entry.Label, GuiTheme.Label, GUILayout.Width(240));
                switch (entry.Kind)
                {
                    case SettingKind.Toggle:
                    {
                        bool cur = _model.Settings.Get(entry.Key, entry.BoolDefault);
                        bool next = GUILayout.Toggle(cur, cur ? " On" : " Off", GuiTheme.Toggle);
                        if (next != cur) _model.Settings.Set(entry.Key, next);
                        break;
                    }
                    case SettingKind.FloatSlider:
                    {
                        float cur = _model.Settings.Get(entry.Key, entry.FloatDefault);
                        float next = GUILayout.HorizontalSlider(cur, entry.Min, entry.Max, GUILayout.Width(280));
                        if (Mathf.Abs(next - cur) > 0.0001f) _model.Settings.Set(entry.Key, next);
                        GUILayout.Label(next.ToString("0.00"), GuiTheme.Label, GUILayout.Width(50));
                        break;
                    }
                    case SettingKind.IntSlider:
                    {
                        int cur = _model.Settings.Get(entry.Key, entry.IntDefault);
                        int next = Mathf.RoundToInt(GUILayout.HorizontalSlider(cur, entry.Min, entry.Max, GUILayout.Width(280)));
                        if (next != cur) _model.Settings.Set(entry.Key, next);
                        GUILayout.Label(next.ToString(), GuiTheme.Label, GUILayout.Width(50));
                        break;
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(3);
            }
        }

        private void DrawPresetPanel()
        {
            GUILayout.Space(14);
            GUILayout.Label("PRESETS", GuiTheme.Section);
            GUILayout.BeginHorizontal();
            int col = 0;
            foreach (var name in _model.Presets.BuiltInPresetNames)
            {
                if (GUILayout.Button(name, name == _model.SelectedPreset ? GuiTheme.ButtonAccent : GuiTheme.Button, GUILayout.Height(28), GUILayout.Width(120)))
                    _model.ApplyPreset(name);
                col++;
                if (col % 5 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Save as:", GuiTheme.Label, GUILayout.Width(60));
            _saveName = GUILayout.TextField(_saveName, GuiTheme.Search, GUILayout.Width(180));
            if (GUILayout.Button("Save Preset", GuiTheme.Button, GUILayout.Width(110)))
                _model.SaveCurrentAs(_saveName);
            GUILayout.EndHorizontal();

            if (_model.Presets.UserPresetNames.Count > 0)
            {
                GUILayout.Label("User Presets:", GuiTheme.Label);
                GUILayout.BeginHorizontal();
                foreach (var name in _model.Presets.UserPresetNames)
                {
                    if (GUILayout.Button(name, name == _model.SelectedPreset ? GuiTheme.ButtonAccent : GuiTheme.Button, GUILayout.Height(26)))
                    {
                        if (_model.Presets.TryLoadUser(name, out var cfg))
                        {
                            _model.Presets.Apply(cfg);
                            _model.SelectedPreset = name;
                            _model.SetStatus($"Loaded user preset: {name}");
                        }
                        else
                        {
                            _model.SetStatus($"Failed to load: {name}");
                        }
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void ApplyRebind(KeyCode key)
        {
            if (key == KeyCode.Escape)
            {
                _rebinding = false;
                _model.SetStatus("Rebind cancelled");
                return;
            }
            _model.Settings.Set("gui.toggleKey", (int)key);
            _toggleKey = key;
            _rebinding = false;
            _model.SetStatus("Rebound GUI to " + key);
        }

        private void DrawPerfHud()
        {
            var p = _model.Performance;
            string fps = _model.Settings.Get("debug.showFps", true)
                ? $"FPS {p.CurrentFps:0}  ·  GPU {p.EstimatedGpuMs:0.00}ms  ·  {p.CurrentTier.Name}"
                : p.CurrentTier.Name;
            GUILayout.Label(fps, GuiTheme.SubHeader);
        }

        private void HandleResize()
        {
            var handle = new Rect(_window.xMax - 22, _window.yMax - 22, 22, 22);
            if (Event.current.type == EventType.MouseDown && handle.Contains(Event.current.mousePosition))
                _resizing = true;
            if (_resizing && Event.current.type == EventType.MouseDrag)
            {
                _window.width = Mathf.Clamp(_window.width + Event.current.delta.x, MinW, MaxW);
                _window.height = Mathf.Clamp(_window.height + Event.current.delta.y, MinH, MaxH);
            }
            if (Event.current.type == EventType.MouseUp)
                _resizing = false;

            // Corner grip visual
            if (Event.current.type == EventType.Repaint)
            {
                var r = new Rect(_window.xMax - 14, _window.yMax - 14, 10, 10);
                UnityEngine.GUI.color = GuiTheme.Accent;
                UnityEngine.GUI.DrawTexture(r, Texture2D.whiteTexture);
                UnityEngine.GUI.color = Color.white;
            }
        }

        private void ClampWindowToScreen()
        {
            _window.width = Mathf.Clamp(_window.width, MinW, MaxW);
            _window.height = Mathf.Clamp(_window.height, MinH, MaxH);
            float maxX = Mathf.Max(8f, Screen.width - _window.width - 8f);
            float maxY = Mathf.Max(8f, Screen.height - _window.height - 8f);
            _window.x = Mathf.Clamp(_window.x, 8f, maxX);
            _window.y = Mathf.Clamp(_window.y, 8f, maxY);
        }

        private void ExportConfig()
        {
            try
            {
                var cfg = _model.Presets.CaptureCurrent();
                var json = _model.Presets.ExportJson(cfg);
                var path = Path.Combine(PathUtil.ConfigDir, $"export_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(path, json, Encoding.UTF8);
                GUIUtility.systemCopyBuffer = json;
                _model.SetStatus($"Exported to {path} (copied)");
            }
            catch (Exception ex)
            {
                _model.SetStatus("Export failed: " + ex.Message);
            }
        }

        private void PersistWindow()
        {
            if (_model == null) return;
            ClampWindowToScreen();
            _model.Settings.Set("gui.x", _window.x);
            _model.Settings.Set("gui.y", _window.y);
            _model.Settings.Set("gui.w", Mathf.Max(MinW, _window.width));
            _model.Settings.Set("gui.h", Mathf.Max(MinH, _window.height));
            _model.Settings.Save();
        }

        private void OnDestroy()
        {
            PersistWindow();
            if (_colorPreview != null)
                Destroy(_colorPreview);
        }
    }
}
