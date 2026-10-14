using System;
using System.Globalization;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace RoboticsMod
{
    /// <summary>
    /// Local test channel for automated verification: reads commands from
    /// LocalLow/Stonext Games/Flyout/Robotics/cmd.txt and writes status.json next to it.
    /// Commands: shot NAME | status | hold PCT | release | kalplay | openkal | closekal | preview PCT
    /// </summary>
    public static class TestChannel
    {
        static string _dir;
        static float _next, _nextStatus;
        static float? _hold;
        static bool _active; // status.json is only written once a test command has been received

        static string Dir
        {
            get
            {
                if (_dir == null)
                {
                    _dir = Path.Combine(Application.persistentDataPath, "Robotics");
                    Directory.CreateDirectory(_dir);
                }
                return _dir;
            }
        }

        public static void Update()
        {
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 0.3f;
                // developer-only: inactive unless the Robotics test folder already exists
                if (_dir == null && !Directory.Exists(Path.Combine(Application.persistentDataPath, "Robotics"))) { _next = Time.unscaledTime + 5f; return; }
                string cmdFile = Path.Combine(Dir, "cmd.txt");
                if (File.Exists(cmdFile))
                {
                    string[] lines;
                    try { lines = File.ReadAllLines(cmdFile); File.Delete(cmdFile); }
                    catch { lines = Array.Empty<string>(); }
                    _active = true;
                    foreach (var l in lines) Run(l.Trim());
                }
            }
            if (_hold.HasValue)
            {
                foreach (var c in UnityEngine.Object.FindObjectsOfType<HydraulicCylinder>())
                {
                    var ip = c.FindInput();
                    if (ip != null) Fields.SetOverride(ip, _hold.Value);
                }
            }
            if (_active && Time.unscaledTime >= _nextStatus) { _nextStatus = Time.unscaledTime + 0.5f; WriteStatus(); }
        }

        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static void Run(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            var a = cmd.Split(' ');
            try
            {
                switch (a[0])
                {
                    case "shot": ScreenCapture.CaptureScreenshot(Path.Combine(Dir, (a.Length > 1 ? a[1] : "shot") + ".png")); break;
                    case "status": WriteStatus(); break;
                    case "hold": _hold = P(a[1]); break;
                    case "release": _hold = null; break;
                    case "kalplay": foreach (var k in UnityEngine.Object.FindObjectsOfType<KalController>()) k.data.TogglePlay(); break;
                    case "openkal": foreach (var k in UnityEngine.Object.FindObjectsOfType<KalController>()) { KalTrackEditor.Open(k); break; } break;
                    case "closekal": KalTrackEditor.Close(); break;
                    case "probe": KalTrackEditor.ProbeRequested = true; break;
                    case "cylhide":
                        foreach (var c in UnityEngine.Object.FindObjectsOfType<HydraulicCylinder>())
                        {
                            c.SetViewHidden(a[1] == "1");
                            MelonLogger.Msg($"[Robotics] cylinder #{c.part?.id} hidden={c.viewHidden} meshes={c.MeshRendererCount()}");
                        }
                        break;
                    case "icons": LibraryIcons.Fix(UnityEngine.Object.FindObjectOfType<Il2CppCraftEditor.PartRef>()); break;
                    case "uilist":
                        foreach (var b in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>())
                        {
                            if (b == null || !b.gameObject.activeInHierarchy) continue;
                            var tx = b.GetComponentInChildren<Il2CppTMPro.TMP_Text>();
                            var p = b.transform.position;
                            MelonLogger.Msg($"[Robotics] button '{b.gameObject.name}' text='{(tx != null ? tx.text : "")}' parent='{b.transform.parent?.name}' pos=({p.x:0},{p.y:0})");
                        }
                        break;
                    case "uiclick":
                        {
                            string want = cmd.Substring(8).Trim();
                            foreach (var b in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>())
                            {
                                if (b == null || !b.gameObject.activeInHierarchy) continue;
                                var tx = b.GetComponentInChildren<Il2CppTMPro.TMP_Text>();
                                if (b.gameObject.name == want || (tx != null && tx.text == want)) { b.onClick.Invoke(); MelonLogger.Msg("[Robotics] clicked " + want); break; }
                            }
                        }
                        break;
                    case "robtab":
                        foreach (var cp in UnityEngine.Object.FindObjectsOfType<Il2CppCraftEditor.CategoryPanel>())
                            for (int i = 0; i < cp.partCatergoryButtons.Count; i++)
                            {
                                var b = cp.partCatergoryButtons[i];
                                var tx = b != null ? b.GetComponentInChildren<Il2CppTMPro.TMP_Text>() : null;
                                if (tx != null && tx.text == "ROB") b.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
                            }
                        break;
                    case "scale":
                        foreach (var c in UnityEngine.Object.FindObjectsOfType<HydraulicCylinder>())
                        {
                            if (c.telescopic) continue;
                            var res = c.GetComponent<Il2Cpp.Resizable>();
                            res?.SetScale(new Vector3(P(a[1]), P(a[2]), P(a[3])), true, false);
                            MelonLogger.Msg($"[Robotics] scale request {a[1]},{a[2]},{a[3]} -> localScale {c.transform.localScale} base lossy {c.Size}");
                            break;
                        }
                        break;
                    case "cylmin":
                        foreach (var c in UnityEngine.Object.FindObjectsOfType<HydraulicCylinder>()) { c.minPct = P(a[1]); if (c.maxPct < c.minPct) c.maxPct = c.minPct; }
                        break;
                    case "cyldump":
                        foreach (var c in UnityEngine.Object.FindObjectsOfType<HydraulicCylinder>()) MelonLogger.Msg("[Robotics] " + c.Dump());
                        break;
                    case "preview": foreach (var c in UnityEngine.Object.FindObjectsOfType<HydraulicCylinder>()) c.SetPreview(P(a[1])); break;
                }
                MelonLogger.Msg("[Robotics] test cmd: " + cmd);
            }
            catch (Exception e) { MelonLogger.Warning($"[Robotics] test cmd '{cmd}' failed: {e.Message}"); }
        }

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";

        static void WriteStatus()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\"time\":").Append(F(Time.time)).Append(",\"scene\":\"").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append("\",\"cylinders\":[");
                bool first = true;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<HydraulicCylinder>())
                {
                    if (c == null || c.part == null || c.part.id < 0) continue;
                    if (!first) sb.Append(','); first = false;
                    float ipv = -1; try { var ip = c.FindInput(); if (ip != null) ipv = ip.value; } catch { }
                    sb.Append("{\"id\":").Append(c.part.id).Append(",\"tele\":").Append(c.telescopic ? "true" : "false")
                      .Append(",\"flight\":").Append(c.isFlight ? "true" : "false").Append(",\"status\":\"").Append(c.status)
                      .Append("\",\"size\":").Append(F(c.Size)).Append(",\"stroke\":").Append(F(c.StrokeWorld))
                      .Append(",\"forceKN\":").Append(F(c.MaxForce / 1000f))
                      .Append(",\"ext01\":").Append(F(c.ext01)).Append(",\"target01\":").Append(F(c.target01))
                      .Append(",\"input\":").Append(F(ipv)).Append(",\"pos\":").Append(V(c.transform.position)).Append('}');
                }
                sb.Append("],\"kals\":[");
                first = true;
                foreach (var k in UnityEngine.Object.FindObjectsOfType<KalController>())
                {
                    if (k == null || k.part == null || k.part.id < 0) continue;
                    if (!first) sb.Append(','); first = false;
                    int linked = 0;
                    for (int i = 0; i < k.data.Tracks.Count; i++) if (k.ProcessorOf(i) != null) linked++;
                    sb.Append("{\"id\":").Append(k.part.id).Append(",\"pos\":").Append(F(k.data.Position)).Append(",\"playing\":")
                      .Append(k.data.Playing ? "true" : "false").Append(",\"dir\":").Append(k.data.Direction)
                      .Append(",\"tracks\":").Append(k.data.Tracks.Count).Append(",\"linked\":").Append(linked).Append('}');
                }
                sb.Append("],\"editorOpen\":").Append(KalTrackEditor.Current != null ? "true" : "false").Append('}');
                File.WriteAllText(Path.Combine(Dir, "status.json"), sb.ToString());
            }
            catch { }
        }
    }
}
