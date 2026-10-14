using System;
using System.Collections.Generic;
using System.Globalization;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace RoboticsMod
{
    /// <summary>KSP-style KAL-1000 track editor, drawn with IMGUI from RoboticsMelonMod.OnGUI.</summary>
    public static class KalTrackEditor
    {
        public static KalController Current;
        public static bool TextFocused;

        static Rect _win = new Rect(0, 0, 860, 380);
        static bool _placed, _draggingWin, _draggingPoint, _picking;
        static Vector2 _dragOffset;
        static int _track = -1, _point = -1;
        static KalTrack _clipboard;
        static List<Fields.Entry> _pickList;

        static Texture2D _white, _graph;
        static bool _graphDirty = true;
        static int _graphW, _graphH, _lastVersion = -1;
        static GUIStyle _title, _label, _small, _btn, _btnOn, _field, _row, _rowSel;
        static string _timeStr = "", _valueStr = "", _lengthStr = "";
        static int _strFor = -2;

        static GameObject _blockerRoot;
        static RectTransform _blocker;

        static readonly Color32 Bg = new Color32(28, 29, 32, 245), HeaderRed = new Color32(128, 18, 22, 255);
        static readonly Color[] Palette =
        {
            new Color(0.25f, 0.85f, 0.35f), new Color(0.30f, 0.65f, 1f), new Color(1f, 0.62f, 0.15f), new Color(0.95f, 0.30f, 0.75f),
            new Color(0.95f, 0.90f, 0.25f), new Color(0.35f, 0.95f, 0.95f), new Color(0.75f, 0.50f, 1f), new Color(1f, 0.35f, 0.30f),
        };

        public static void Open(KalController kal)
        {
            if (kal == null) return;
            Current = kal;
            _track = kal.data.Tracks.Count > 0 ? 0 : -1;
            _point = -1;
            _picking = false;
            _graphDirty = true;
            kal.Resolve(true);
        }

        public static void Close()
        {
            if (Current != null && !Current.isFlight) { Current.data.Playing = false; Current.StopPreview(); }
            Current = null;
            _picking = false;
            TextFocused = false;
            SetBlocker(false, default);
        }

        /// <summary>Just make the window invisible: playback, preview and all edits keep going.
        /// Reopen with the panel button, right-click on the KAL (flight) or Ctrl+K.</summary>
        public static void Hide()
        {
            Current = null;
            _picking = false;
            TextFocused = false;
            SetBlocker(false, default);
        }

        public static bool IsOpenFor(KalController k) => Current != null && k != null && Current.Pointer == k.Pointer;

        // ------------------------------------------------------------------ helpers
        static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                    _white.hideFlags = HideFlags.HideAndDontSave;
                }
                return _white;
            }
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        static void Styles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _title.normal.textColor = Color.white;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _label.normal.textColor = new Color(0.85f, 0.86f, 0.88f);
            _small = new GUIStyle(_label) { fontSize = 10 };
            _small.normal.textColor = new Color(0.6f, 0.62f, 0.65f);
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 12 };
            _btn.normal.background = Solid(new Color(0.16f, 0.17f, 0.19f));
            _btn.hover.background = Solid(new Color(0.24f, 0.25f, 0.28f));
            _btn.active.background = Solid(new Color(0.35f, 0.36f, 0.40f));
            _btn.normal.textColor = _btn.hover.textColor = _btn.active.textColor = new Color(0.9f, 0.9f, 0.92f);
            _btnOn = new GUIStyle(_btn);
            _btnOn.normal.background = Solid(new Color(0.20f, 0.45f, 0.22f));
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            _field.normal.background = Solid(new Color(0.10f, 0.11f, 0.13f));
            _field.focused.background = Solid(new Color(0.14f, 0.15f, 0.18f));
            _field.normal.textColor = _field.focused.textColor = new Color(0.55f, 0.85f, 1f);
            _row = new GUIStyle(_label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false, fontSize = 11 };
            _rowSel = new GUIStyle(_row);
            _rowSel.normal.textColor = Color.white;
            _rowSel.fontStyle = FontStyle.Bold;
        }

        static bool Btn(Rect r, string text, bool on = false) => GUI.Button(r, text, on ? _btnOn : _btn);

        static string F(float v, string fmt = "0.###") => v.ToString(fmt, CultureInfo.InvariantCulture);
        static bool TryF(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        static void SetBlocker(bool on, Rect r)
        {
            try
            {
                if (_blockerRoot == null)
                {
                    if (!on) return;
                    _blockerRoot = new GameObject("RoboticsUIBlocker");
                    UnityEngine.Object.DontDestroyOnLoad(_blockerRoot);
                    var canvas = _blockerRoot.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 30000;
                    _blockerRoot.AddComponent<GraphicRaycaster>();
                    var go = new GameObject("Block");
                    go.transform.SetParent(_blockerRoot.transform, false);
                    var img = go.AddComponent<Image>();
                    img.color = new Color(0, 0, 0, 0);
                    img.raycastTarget = true;
                    _blocker = img.rectTransform;
                    _blocker.anchorMin = _blocker.anchorMax = Vector2.zero;
                    _blocker.pivot = Vector2.zero;
                }
                _blockerRoot.SetActive(on);
                if (on && _blocker != null)
                {
                    var canvas = _blockerRoot.GetComponent<Canvas>();
                    float s = canvas != null && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;
                    _blocker.anchoredPosition = new Vector2(r.x, Screen.height - r.y - r.height) / s;
                    _blocker.sizeDelta = new Vector2(r.width, r.height) / s;
                }
            }
            catch (Exception e) { MelonLogger.Warning("[Robotics] UI blocker: " + e.Message); }
        }

        // ------------------------------------------------------------------ main
        public static bool ProbeRequested;

        /// <summary>Tries every IMGUI call the editor uses once and logs which ones this game build stripped.</summary>
        static void Probe()
        {
            ProbeRequested = false;
            Styles();
            var r = new Rect(-500, -500, 10, 10);
            void T(string n, Action a) { try { a(); MelonLogger.Msg("[Robotics] probe ok: " + n); } catch (Exception ex) { MelonLogger.Warning($"[Robotics] probe FAIL: {n}: {ex.GetType().Name} {ex.Message}"); } }
            T("Label", () => GUI.Label(r, "x", _label));
            T("Button", () => GUI.Button(r, "x", _btn));
            T("DrawTexture", () => GUI.DrawTexture(r, White));
            T("TextField", () => GUI.TextField(r, "x", 5, _field));
            T("SetNextControlName", () => GUI.SetNextControlName("kal_probe"));
            T("GetNameOfFocusedControl", () => GUI.GetNameOfFocusedControl());
            T("HorizontalSlider", () => GUI.HorizontalSlider(r, 0.5f, 0f, 1f));
            T("Event.delta", () => { var d = Event.current.delta; });
            T("clickCount", () => { var c = Event.current.clickCount; });
            T("Texture2D.SetPixels32", () => { var t = new Texture2D(2, 2); t.SetPixels32(new Color32[4]); t.Apply(); UnityEngine.Object.Destroy(t); });
            T("GUIStyle copy", () => { var g = new GUIStyle(_btn); });
        }

        public static void OnGUI()
        {
            if (ProbeRequested) Probe();
            if (Current == null) return;
            if (Current.part == null || Current.gameObject == null) { Close(); return; }
            Styles();
            var d = Current.data;

            if (!_placed)
            {
                _win.x = Mathf.Max(10, (Screen.width - _win.width) / 2f);
                _win.y = Mathf.Max(10, Screen.height - _win.height - 90);
                _placed = true;
            }
            _win.x = Mathf.Clamp(_win.x, 0, Mathf.Max(0, Screen.width - 120));
            _win.y = Mathf.Clamp(_win.y, 0, Mathf.Max(0, Screen.height - 40));
            SetBlocker(true, _win);
            if (Current.isFlight) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }

            if (_track >= d.Tracks.Count) _track = d.Tracks.Count - 1;
            if (_track < 0 && d.Tracks.Count > 0) _track = 0;
            var tr = _track >= 0 ? d.Tracks[_track] : null;
            if (tr == null || _point >= tr.Keys.Count) _point = -1;
            if (_lastVersion != Current.version) { _lastVersion = Current.version; _graphDirty = true; }

            var e = Event.current;
            float x0 = _win.x, y0 = _win.y, W = _win.width, H = _win.height;
            Fill(_win, Bg);

            // ---------- header
            var header = new Rect(x0, y0, W, 30);
            Fill(header, HeaderRed);
            GUI.Label(new Rect(x0 + 8, y0 + 5, 120, 22), "Track Editor", _title);
            float bx = x0 + 130;
            if (Btn(new Rect(bx, y0 + 4, 30, 22), d.Playing ? "||" : "►", d.Playing)) { d.TogglePlay(); }
            if (Btn(new Rect(bx + 32, y0 + 4, 30, 22), "■")) { d.Playing = false; d.Position = d.Direction > 0 ? 0f : d.Length; Changed(false); }
            if (Btn(new Rect(bx + 64, y0 + 4, 30, 22), d.Direction > 0 ? "→" : "←")) d.Direction = -d.Direction;
            if (Btn(new Rect(bx + 104, y0 + 4, 30, 22), "|◄")) { d.Position = 0f; Changed(false); }
            if (Btn(new Rect(bx + 136, y0 + 4, 30, 22), "►|")) { d.Position = d.Length; Changed(false); }
            GUI.SetNextControlName("kal_name");
            d.Name = GUI.TextField(new Rect(bx + 180, y0 + 4, 210, 22), d.Name ?? "", 40, _field);
            GUI.Label(new Rect(bx + 400, y0 + 6, 50, 20), "Length", _title);
            if (GUI.GetNameOfFocusedControl() != "kal_len") _lengthStr = F(d.Length, "0.##");
            GUI.SetNextControlName("kal_len");
            string ls = GUI.TextField(new Rect(bx + 458, y0 + 4, 52, 22), _lengthStr, 8, _field);
            if (ls != _lengthStr) { _lengthStr = ls; if (TryF(ls, out var lv) && lv >= 0.1f) { d.Length = Mathf.Min(lv, 3600f); _graphDirty = true; } }
            string[] loops = { "Once", "Repeat", "Ping-Pong" };
            if (Btn(new Rect(bx + 516, y0 + 4, 78, 22), loops[(int)d.Loop], d.Loop != LoopMode.Once)) d.Loop = (LoopMode)(((int)d.Loop + 1) % 3);
            if (Btn(new Rect(x0 + W - 74, y0 + 4, 40, 22), "Hide")) { Hide(); e.Use(); return; }
            if (Btn(new Rect(x0 + W - 30, y0 + 4, 24, 22), "X")) { Close(); e.Use(); return; }

            // ---------- toolbar
            float ty = y0 + 34;
            GUI.Label(new Rect(x0 + 8, ty + 2, 36, 20), "Row:", _label);
            float tx = x0 + 44;
            bool hasTr = tr != null;
            if (Btn(new Rect(tx, ty, 26, 22), "▲") && hasTr && _track > 0) { Swap(d.Tracks, _track, _track - 1); _track--; Current.TracksChanged(); }
            if (Btn(new Rect(tx + 28, ty, 26, 22), "▼") && hasTr && _track < d.Tracks.Count - 1) { Swap(d.Tracks, _track, _track + 1); _track++; Current.TracksChanged(); }
            if (Btn(new Rect(tx + 56, ty, 44, 22), "Copy") && hasTr) _clipboard = tr.Clone();
            if (Btn(new Rect(tx + 102, ty, 44, 22), "Paste") && hasTr && _clipboard != null) { PasteInto(tr, _clipboard, d.Length); Changed(true); }
            if (Btn(new Rect(tx + 148, ty, 44, 22), "Flip↕") && hasTr) { foreach (var k in tr.Keys) k.Value = tr.Min + tr.Max - k.Value; Changed(true); }
            if (Btn(new Rect(tx + 194, ty, 44, 22), "Flip↔") && hasTr) { foreach (var k in tr.Keys) k.Time = Mathf.Max(0, d.Length - k.Time); tr.Sort(); _point = -1; Changed(true); }
            if (Btn(new Rect(tx + 240, ty, 44, 22), "Clear") && hasTr) { tr.Keys.Clear(); _point = -1; Changed(true); }
            if (Btn(new Rect(tx + 286, ty, 50, 22), "Delete") && hasTr) { d.Tracks.RemoveAt(_track); _track = Mathf.Min(_track, d.Tracks.Count - 1); _point = -1; Current.TracksChanged(); Changed(true); return; }

            GUI.Label(new Rect(tx + 350, ty + 2, 44, 20), "Point:", _label);
            float px = tx + 396;
            if (Btn(new Rect(px, ty, 26, 22), "+") && hasTr)
            {
                float v = tr.Keys.Count > 0 ? tr.Evaluate(d.Position) : CurrentValue(_track, tr);
                _point = tr.AddKey(Mathf.Clamp(d.Position, 0, d.Length), v);
                Changed(true);
            }
            if (Btn(new Rect(px + 28, ty, 26, 22), "-") && hasTr && _point >= 0) { tr.Keys.RemoveAt(_point); _point = -1; Changed(true); }
            KeyMode? mode = hasTr && _point >= 0 ? tr.Keys[_point].Mode : (KeyMode?)null;
            if (Btn(new Rect(px + 60, ty, 54, 22), "Smooth", mode == KeyMode.Smooth) && mode != null) { tr.Keys[_point].Mode = KeyMode.Smooth; Changed(true); }
            if (Btn(new Rect(px + 116, ty, 50, 22), "Linear", mode == KeyMode.Linear) && mode != null) { tr.Keys[_point].Mode = KeyMode.Linear; Changed(true); }
            if (Btn(new Rect(px + 168, ty, 40, 22), "Flat", mode == KeyMode.Flat) && mode != null) { tr.Keys[_point].Mode = KeyMode.Flat; Changed(true); }

            GUI.Label(new Rect(x0 + W - 210, ty + 2, 46, 20), "Speed", _label);
            d.SpeedPct = Mathf.Round(GUI.HorizontalSlider(new Rect(x0 + W - 162, ty + 6, 110, 16), d.SpeedPct, 0f, 200f));
            GUI.Label(new Rect(x0 + W - 46, ty + 2, 44, 20), F(d.SpeedPct, "0") + "%", _label);

            // ---------- regions
            var list = new Rect(x0 + 8, y0 + 82, 220, H - 82 - 36);
            var graph = new Rect(x0 + 236, y0 + 82, W - 236 - 10, H - 82 - 36);
            var scrub = new Rect(graph.x, y0 + 62, graph.width, 16);

            float newPos = GUI.HorizontalSlider(scrub, d.Position, 0f, Mathf.Max(0.01f, d.Length));
            if (Mathf.Abs(newPos - d.Position) > 1e-5f) { d.Position = newPos; Changed(false); }

            DrawList(list, d);
            if (_picking) DrawPicker(graph, d);
            else DrawGraph(graph, d, e);

            // ---------- footer
            float fy = y0 + H - 30;
            tr = _track >= 0 && _track < d.Tracks.Count ? d.Tracks[_track] : null;
            if (tr != null && _point >= tr.Keys.Count) _point = -1;
            var key = tr != null && _point >= 0 ? tr.Keys[_point] : null;
            GUI.Label(new Rect(x0 + 8, fy + 2, 240, 22), tr != null ? $"{tr.Title}  [{F(tr.Min, "0.#")} .. {F(tr.Max, "0.#")}]   now {F(tr.Keys.Count > 0 ? tr.Evaluate(d.Position) : CurrentValue(_track, tr), "0.##")}" : "Add fields to control with \"+ Add field\"", _small);
            if (tr != null && Btn(new Rect(x0 + 250, fy + 1, 90, 22), tr.Symmetry ? "Symmetry: on" : "Symmetry: off", tr.Symmetry)) tr.Symmetry = !tr.Symmetry;
            GUI.Label(new Rect(x0 + W - 430, fy + 2, 46, 22), "Index:", _label);
            GUI.Label(new Rect(x0 + W - 384, fy + 2, 40, 22), key != null ? _point.ToString() : "", _label);
            GUI.Label(new Rect(x0 + W - 340, fy + 2, 40, 22), "Time:", _label);
            GUI.Label(new Rect(x0 + W - 190, fy + 2, 46, 22), "Value:", _label);
            int sid = key != null ? _track * 10000 + _point : -1;
            if (_strFor != sid && !_draggingPoint) { _strFor = sid; _timeStr = key != null ? F(key.Time) : ""; _valueStr = key != null ? F(key.Value) : ""; }
            if (_draggingPoint && key != null) { _timeStr = F(key.Time); _valueStr = F(key.Value); }
            GUI.SetNextControlName("kal_t");
            string ts = GUI.TextField(new Rect(x0 + W - 300, fy, 100, 22), _timeStr, 10, _field);
            GUI.SetNextControlName("kal_v");
            string vs = GUI.TextField(new Rect(x0 + W - 142, fy, 130, 22), _valueStr, 12, _field);
            if (key != null)
            {
                if (ts != _timeStr) { _timeStr = ts; if (TryF(ts, out var tv)) { key.Time = Mathf.Clamp(tv, 0, d.Length); tr.Sort(); _point = tr.Keys.IndexOf(key); _strFor = _track * 10000 + _point; Changed(true); } }
                if (vs != _valueStr) { _valueStr = vs; if (TryF(vs, out var vv)) { key.Value = Clamp(tr, vv); Changed(true); } }
            }

            string focus = GUI.GetNameOfFocusedControl();
            TextFocused = !string.IsNullOrEmpty(focus) && focus.StartsWith("kal_");

            // ---------- window drag: buttons already consumed their clicks, anything left on the header drags
            if (e.type == EventType.MouseDown && header.Contains(e.mousePosition))
            { _draggingWin = true; _dragOffset = e.mousePosition - new Vector2(_win.x, _win.y); e.Use(); }
            if (_draggingWin && e.type == EventType.MouseDrag) { _win.x = e.mousePosition.x - _dragOffset.x; _win.y = e.mousePosition.y - _dragOffset.y; e.Use(); }
            if (e.type == EventType.MouseUp) { _draggingWin = false; _draggingPoint = false; }
        }

        static float Clamp(KalTrack t, float v) => Mathf.Clamp(v, Mathf.Min(t.Min, t.Max), Mathf.Max(t.Min, t.Max));

        static float CurrentValue(int track, KalTrack t)
        {
            try { var ip = Current.ProcessorOf(track); if (ip != null) return ip.value; } catch { }
            return t.Min;
        }

        static void Swap<T>(List<T> l, int a, int b) { var x = l[a]; l[a] = l[b]; l[b] = x; }

        static void PasteInto(KalTrack dst, KalTrack src, float length)
        {
            dst.Keys.Clear();
            float sSpan = src.Max - src.Min, dSpan = dst.Max - dst.Min;
            foreach (var k in src.Keys)
            {
                float n = Mathf.Abs(sSpan) < 1e-6f ? 0f : (k.Value - src.Min) / sSpan;
                dst.Keys.Add(new KalKey { Time = Mathf.Min(k.Time, length), Value = dst.Min + n * dSpan, Mode = k.Mode });
            }
            dst.Sort();
        }

        /// <summary>Data changed: redraw the graph and, in the editor, preview the result on the parts.</summary>
        static void Changed(bool curves)
        {
            if (curves) _graphDirty = true;
            if (Current != null && !Current.isFlight) Current.Apply(true);
        }

        // ------------------------------------------------------------------ track list
        // GUI.BeginScrollView is stripped from this game build: lists page with the mouse wheel instead.
        static int _listFirst, _pickFirst;

        static int Wheel(Rect r, int first, int count, int visible)
        {
            var e = Event.current;
            if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition))
            {
                first += e.delta.y > 0 ? 1 : -1;
                e.Use();
            }
            return Mathf.Clamp(first, 0, Mathf.Max(0, count - visible));
        }

        static void DrawList(Rect r, KalData d)
        {
            Fill(r, new Color(0.12f, 0.13f, 0.15f));
            float rowH = 22;
            var inner = new Rect(r.x, r.y, r.width, r.height - 26);
            int visible = Mathf.Max(1, (int)(inner.height / rowH));
            _listFirst = Wheel(inner, _listFirst, d.Tracks.Count, visible);
            for (int i = _listFirst; i < d.Tracks.Count && i < _listFirst + visible; i++)
            {
                var t = d.Tracks[i];
                var row = new Rect(inner.x, inner.y + (i - _listFirst) * rowH, inner.width, rowH);
                if (i == _track) Fill(row, new Color(0.25f, 0.27f, 0.32f));
                var ip = Current.ProcessorOf(i);
                Fill(new Rect(row.x + 4, row.y + 6, 10, 10), Palette[i % Palette.Length] * (t.Enabled ? 1f : 0.4f));
                if (GUI.Button(new Rect(row.x + 18, row.y + 3, 16, 16), t.Enabled ? "✓" : "", _btn)) { t.Enabled = !t.Enabled; _graphDirty = true; }
                string name = $"{t.PartName}: {t.Title}" + (ip == null ? "  (missing)" : "");
                if (GUI.Button(new Rect(row.x + 38, row.y, row.width - 40, rowH), name, i == _track ? _rowSel : _row))
                { _track = i; _point = -1; _picking = false; _graphDirty = true; }
            }
            if (d.Tracks.Count > visible) GUI.Label(new Rect(inner.xMax - 60, inner.yMax - 16, 58, 16), $"{_listFirst + 1}-{Mathf.Min(d.Tracks.Count, _listFirst + visible)}/{d.Tracks.Count}", _small);
            if (Btn(new Rect(r.x, r.yMax - 24, r.width, 24), _picking ? "Cancel" : "+ Add field", _picking))
            {
                _picking = !_picking;
                if (_picking) { _pickList = Fields.Available(Current.part); _pickFirst = 0; }
            }
        }

        static void DrawPicker(Rect r, KalData d)
        {
            Fill(r, new Color(0.10f, 0.11f, 0.13f));
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, 20), "Pick an input to control (scroll for more):", _label);
            var inner = new Rect(r.x + 4, r.y + 26, r.width - 8, r.height - 30);
            var items = new List<Fields.Entry>();
            if (_pickList != null)
                foreach (var en in _pickList)
                    if (!(en.Part != null && Current.part != null && en.Part.Pointer == Current.part.Pointer)) items.Add(en);
            int visible = Mathf.Max(1, (int)(inner.height / 24));
            _pickFirst = Wheel(inner, _pickFirst, items.Count, visible);
            for (int i = _pickFirst; i < items.Count && i < _pickFirst + visible; i++)
            {
                var en = items[i];
                float mn = 0, mx = 1;
                try { mn = en.Proc.min; mx = en.Proc.max; } catch { }
                if (GUI.Button(new Rect(inner.x, inner.y + (i - _pickFirst) * 24, inner.width, 22), $"{en.PartName}  -  {en.Title}   [{F(mn, "0.#")} .. {F(mx, "0.#")}]", _btn))
                {
                    var t = new KalTrack { Tag = RoboticsTag.Ensure(en.Part), PartId = en.Part.id, ProcessorIndex = en.Index, Title = en.Title, PartName = en.PartName, Min = mn, Max = mx };
                    float cur = mn;
                    try { cur = en.Proc.value; } catch { }
                    t.AddKey(0f, Clamp(t, cur));
                    d.Tracks.Add(t);
                    _track = d.Tracks.Count - 1; _point = 0; _picking = false;
                    Current.TracksChanged(); _graphDirty = true;
                }
            }
            if (items.Count == 0) GUI.Label(new Rect(inner.x + 4, inner.y + 4, inner.width, 22), "No inputs found. Place cylinders/hinges first.", _label);
        }

        // ------------------------------------------------------------------ graph
        static float TX(Rect g, float t, float len) => g.x + (len <= 0 ? 0 : t / len) * g.width;
        static float VY(Rect g, KalTrack tr, float v)
        {
            float span = tr.Max - tr.Min;
            float n = Mathf.Abs(span) < 1e-6f ? 0.5f : (v - tr.Min) / span;
            return g.yMax - 6 - n * (g.height - 12);
        }

        static void DrawGraph(Rect g, KalData d, Event e)
        {
            int w = Mathf.Max(8, (int)g.width), h = Mathf.Max(8, (int)g.height);
            if (_graph == null || _graphW != w || _graphH != h || _graphDirty) Rasterize(w, h, d);
            GUI.DrawTexture(g, _graph);

            var tr = _track >= 0 && _track < d.Tracks.Count ? d.Tracks[_track] : null;
            // axis labels for the selected row
            if (tr != null)
            {
                GUI.Label(new Rect(g.x + 3, g.y + 1, 80, 16), F(tr.Max, "0.##"), _small);
                GUI.Label(new Rect(g.x + 3, g.yMax - 17, 80, 16), F(tr.Min, "0.##"), _small);
            }
            GUI.Label(new Rect(g.xMax - 60, g.yMax - 17, 58, 16), F(d.Length, "0.#") + " s", _small);

            // keyframes
            for (int ti = 0; ti < d.Tracks.Count; ti++)
            {
                var t = d.Tracks[ti];
                bool sel = ti == _track;
                for (int k = 0; k < t.Keys.Count; k++)
                {
                    float px = TX(g, t.Keys[k].Time, d.Length), py = VY(g, t, t.Keys[k].Value);
                    float s = sel ? 8 : 5;
                    Color c = sel && k == _point ? Color.white : Palette[ti % Palette.Length] * (sel ? 1f : 0.6f);
                    Fill(new Rect(px - s / 2, py - s / 2, s, s), c);
                }
            }
            // play head
            float hx = TX(g, Mathf.Clamp(d.Position, 0, d.Length), d.Length);
            Fill(new Rect(hx - 1, g.y, 2, g.height), new Color(1f, 1f, 1f, 0.85f));

            // mouse
            if (!g.Contains(e.mousePosition) && !_draggingPoint) return;
            Vector2 m = e.mousePosition;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // nearest point (selected track first)
                int bestT = -1, bestK = -1; float best = 81f;
                for (int pass = 0; pass < 2 && bestT < 0; pass++)
                    for (int ti = 0; ti < d.Tracks.Count; ti++)
                    {
                        if ((pass == 0) != (ti == _track)) continue;
                        var t = d.Tracks[ti];
                        for (int k = 0; k < t.Keys.Count; k++)
                        {
                            float dx = TX(g, t.Keys[k].Time, d.Length) - m.x, dy = VY(g, t, t.Keys[k].Value) - m.y;
                            float dd = dx * dx + dy * dy;
                            if (dd < best) { best = dd; bestT = ti; bestK = k; }
                        }
                    }
                if (bestT >= 0)
                {
                    if (bestT != _track) _graphDirty = true;
                    _track = bestT; _point = bestK; _draggingPoint = true;
                }
                else if (e.clickCount >= 2 && tr != null)
                {
                    float t = Mathf.Clamp((m.x - g.x) / g.width * d.Length, 0, d.Length);
                    float n = Mathf.Clamp01((g.yMax - 6 - m.y) / (g.height - 12));
                    _point = tr.AddKey(t, tr.Min + n * (tr.Max - tr.Min));
                    Changed(true);
                }
                else
                {
                    d.Position = Mathf.Clamp((m.x - g.x) / g.width * d.Length, 0, d.Length);
                    _point = -1;
                    Changed(false);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingPoint && tr != null && _point >= 0 && _point < tr.Keys.Count)
            {
                var key = tr.Keys[_point];
                key.Time = Mathf.Clamp((m.x - g.x) / g.width * d.Length, 0, d.Length);
                float n = Mathf.Clamp01((g.yMax - 6 - m.y) / (g.height - 12));
                key.Value = tr.Min + n * (tr.Max - tr.Min);
                tr.Sort();
                _point = tr.Keys.IndexOf(key);
                Changed(true);
                e.Use();
            }
        }

        static void Rasterize(int w, int h, KalData d)
        {
            if (_graph == null || _graphW != w || _graphH != h)
            {
                if (_graph != null) UnityEngine.Object.Destroy(_graph);
                _graph = new Texture2D(w, h, TextureFormat.RGBA32, false);
                _graph.filterMode = FilterMode.Point;
                _graph.wrapMode = TextureWrapMode.Clamp;
                _graph.hideFlags = HideFlags.HideAndDontSave;
                _graphW = w; _graphH = h;
            }
            var px = new Color32[w * h];
            var bg = new Color32(20, 21, 24, 255);
            var grid = new Color32(44, 46, 52, 255);
            var gridMajor = new Color32(62, 65, 72, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            // texture row 0 is the bottom
            float len = Mathf.Max(0.01f, d.Length);
            float step = len <= 6 ? 0.5f : len <= 15 ? 1f : len <= 40 ? 2f : len <= 120 ? 10f : 30f;
            for (float t = 0; t <= len + 1e-4f; t += step)
            {
                int x = Mathf.Clamp((int)(t / len * (w - 1)), 0, w - 1);
                bool major = Mathf.Abs(t / (step * 2) - Mathf.Round(t / (step * 2))) < 1e-3f;
                for (int y = 0; y < h; y++) px[y * w + x] = major ? gridMajor : grid;
            }
            for (int j = 0; j <= 4; j++)
            {
                int y = 6 + (int)(j / 4f * (h - 13));
                for (int x = 0; x < w; x++) px[y * w + x] = j == 0 || j == 4 ? gridMajor : grid;
            }

            for (int pass = 0; pass < 2; pass++)
                for (int ti = 0; ti < d.Tracks.Count; ti++)
                {
                    bool sel = ti == _track;
                    if ((pass == 1) != sel) continue; // selected track drawn last, on top
                    var t = d.Tracks[ti];
                    if (t.Keys.Count == 0) continue;
                    Color c = Palette[ti % Palette.Length] * (t.Enabled ? (sel ? 1f : 0.55f) : 0.25f);
                    var c32 = (Color32)new Color(c.r, c.g, c.b, 1f);
                    int thick = sel ? 1 : 0;
                    float span = t.Max - t.Min;
                    int prevY = -1;
                    for (int x = 0; x < w; x++)
                    {
                        float v = t.Evaluate(x / (float)(w - 1) * len);
                        float n = Mathf.Abs(span) < 1e-6f ? 0.5f : (v - t.Min) / span;
                        int y = Mathf.Clamp(6 + (int)Mathf.Round(n * (h - 13)), 0, h - 1);
                        int a = prevY < 0 ? y : Mathf.Min(prevY, y), b = prevY < 0 ? y : Mathf.Max(prevY, y);
                        for (int yy = a - thick; yy <= b + thick; yy++)
                            for (int xx = x - thick; xx <= x + thick; xx++)
                                if (xx >= 0 && xx < w && yy >= 0 && yy < h) px[yy * w + xx] = c32;
                        prevY = y;
                    }
                }
            _graph.SetPixels32(px);
            _graph.Apply(false);
            _graphDirty = false;
        }
    }
}
