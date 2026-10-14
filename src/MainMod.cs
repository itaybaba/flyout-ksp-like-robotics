using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppCraftEditor;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMods;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using InputProcessor = Il2Cpp.InputProcessor;

[assembly: MelonInfo(typeof(RoboticsMod.RoboticsMelonMod), "KSP-Like Robotics", "1.0.0", "itaybaba", "https://github.com/itaybaba/flyout-ksp-like-robotics")]
[assembly: MelonGame("Stonext Games", "Flyout")]

namespace RoboticsMod
{
    public class RoboticsMelonMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            PartFiles.Install();
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<HydraulicCylinder>();
                ClassInjector.RegisterTypeInIl2Cpp<KalController>();
                ClassInjector.RegisterTypeInIl2Cpp<CylinderPanel>();
                ClassInjector.RegisterTypeInIl2Cpp<KalPanel>();
                ClassInjector.RegisterTypeInIl2Cpp<RoboticsTag>();
                MelonLogger.Msg("Registered HydraulicCylinder, KalController and their panels");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to register IL2CPP types: {ex}");
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            KalTrackEditor.Close();
            Fields.ClearOverrides();
        }

        public override void OnGUI()
        {
            try { KalTrackEditor.OnGUI(); }
            catch (Exception e) { MelonLogger.Warning("[Robotics] track editor: " + e); KalTrackEditor.Close(); }
        }

        public override void OnUpdate()
        {
            try { FlightOpenEditor(); } catch { }
            TestChannel.Update();
        }

        // In flight: right-click a KAL-1000 (or Ctrl+K for the nearest one) opens its track editor.
        static void FlightOpenEditor()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                var cam = Camera.main;
                if (cam == null) return;
                var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, 10000f))
                {
                    var kal = hit.collider != null ? hit.collider.GetComponentInParent<KalController>() : null;
                    if (kal != null && kal.isFlight) KalTrackEditor.Open(kal);
                }
            }
            if (kb != null && kb.ctrlKey.isPressed && kb.kKey.wasPressedThisFrame && !KalTrackEditor.TextFocused)
            {
                if (KalTrackEditor.Current != null) { KalTrackEditor.Close(); return; }
                var cam = Camera.main;
                KalController best = null; float bestD = float.MaxValue;
                foreach (var k in UnityEngine.Object.FindObjectsOfType<KalController>())
                {
                    if (k == null || !k.isFlight) continue;
                    float dd = cam != null ? (k.transform.position - cam.transform.position).sqrMagnitude : 0f;
                    if (dd < bestD) { bestD = dd; best = k; }
                }
                if (best != null) KalTrackEditor.Open(best);
            }
        }
    }

    public enum RoboticKind { None, Cylinder, Telescopic, Kal }

    public static class Robotics
    {
        public static RoboticKind KindOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return RoboticKind.None;
            if (path.IndexOf("RoboticsHydraulicCylinder", StringComparison.OrdinalIgnoreCase) >= 0) return RoboticKind.Cylinder;
            if (path.IndexOf("RoboticsTelescopicCylinder", StringComparison.OrdinalIgnoreCase) >= 0) return RoboticKind.Telescopic;
            if (path.IndexOf("RoboticsKAL1000", StringComparison.OrdinalIgnoreCase) >= 0) return RoboticKind.Kal;
            return RoboticKind.None;
        }

        public static RoboticKind KindOf(Part p)
        {
            if (p == null) return RoboticKind.None;
            var k = KindOf(p.resourcePath);
            if (k == RoboticKind.None) k = KindOf(p.gameObject.name);
            return k;
        }

        static void AddModule(Part part, PartModule m)
        {
            var list = new List<PartModule>();
            if (part.modules != null)
                foreach (var x in part.modules)
                    if (x != null && x.Pointer != m.Pointer) list.Add(x);
            list.Add(m);
            part.modules = new Il2CppReferenceArray<PartModule>(list.ToArray());
        }

        public static void EnsureModule(Part part, PartModule m)
        {
            if (!HasModule(part, m)) AddModule(part, m);
        }

        static bool HasModule(Part part, PartModule m)
        {
            if (part.modules == null) return false;
            foreach (var x in part.modules) if (x != null && x.Pointer == m.Pointer) return true;
            return false;
        }

        public static void Attach(Part part, RoboticKind kind)
        {
            if (part == null || kind == RoboticKind.None) return;
            var go = part.gameObject;
            if (kind == RoboticKind.Kal)
            {
                var kal = go.GetComponent<KalController>();
                if (kal == null) kal = go.AddComponent<KalController>();
                kal.part = part;
                if (!HasModule(part, kal)) AddModule(part, kal);
                return;
            }

            var cyl = go.GetComponent<HydraulicCylinder>();
            if (cyl == null) cyl = go.AddComponent<HydraulicCylinder>();
            cyl.part = part;
            cyl.Setup(kind == RoboticKind.Telescopic);
            if (!HasModule(part, cyl)) AddModule(part, cyl);

            // the game's own input module: same key/axis binding UI as hinges, and what KAL-1000 drives
            var ip = go.GetComponent<InputProcessor>();
            if (ip == null)
            {
                ip = go.AddComponent<InputProcessor>();
                ip.title = "Extension";
                ip.outputType = InputProcessor.OutputType.Units;
                ip.min = 0f;
                ip.max = 100f;
                ip.offset = 0f;
                ip.speed = 50f;
                ip.value = 0f;
                ip.responses = new Il2CppStructArray<InputProcessor.Response>(0);
            }
            ip.part = part;
            if (!HasModule(part, ip)) AddModule(part, ip);

            var res = go.GetComponent<Resizable>();
            if (res != null) res.keepRelativeScale = true;
        }
    }

    // ---------------------------------------------------------------- editor tab
    [HarmonyPatch(typeof(CategoryPanel), nameof(CategoryPanel.CategoryShort))]
    public static class CategoryShortPatch
    {
        public static void Postfix(Category cat, ref string __result)
        {
            if (cat == Category.Joint) __result = "ROB";
        }
    }

    [HarmonyPatch(typeof(CategoryPanel), nameof(CategoryPanel.ListPartMenuCatergories))]
    public static class CategoryPanelListPatch
    {
        public static void Postfix(CategoryPanel __instance)
        {
            try
            {
                if (__instance == null || __instance.partCatergoryButtons == null) return;
                for (int i = 0; i < __instance.partCatergoryButtons.Count; i++)
                {
                    var btn = __instance.partCatergoryButtons[i];
                    if (btn == null) continue;
                    var txt = btn.GetComponentInChildren<Il2CppTMPro.TMP_Text>();
                    if (txt == null) continue;
                    LibraryIcons.Fix(UnityEngine.Object.FindObjectOfType<PartRef>());
                    if (txt.text == "JNT" || txt.text == "Joint" || txt.text == "ROB")
                    {
                        txt.text = "ROB";
                        var tip = btn.GetComponent<ButtonTooltip>();
                        if (tip != null) tip.text = "Robotics";
                    }
                }
            }
            catch (Exception ex) { MelonLogger.Warning($"CategoryPanel patch: {ex.Message}"); }
        }
    }

    // ---------------------------------------------------------------- module attachment
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.Instantiate))]
    public static class ModManagerInstantiatePatch
    {
        public static void Postfix(string configPath, GameObject __result)
        {
            try
            {
                var kind = Robotics.KindOf(configPath);
                if (kind == RoboticKind.None || __result == null) return;
                var part = __result.GetComponent<Part>();
                if (part != null) Robotics.Attach(part, kind);
            }
            catch (Exception ex) { MelonLogger.Warning($"[Robotics] Instantiate patch: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Part), nameof(Part.Awake))]
    public static class PartAwakePatch
    {
        public static void Postfix(Part __instance)
        {
            try
            {
                var kind = Robotics.KindOf(__instance);
                if (kind != RoboticKind.None) Robotics.Attach(__instance, kind);
            }
            catch (Exception ex) { MelonLogger.Warning($"[Robotics] Part.Awake patch: {ex.Message}"); }
        }
    }

    // ---------------------------------------------------------------- craft loading
    // Part.ContructFromData resolves "*ModuleName" blocks with Type.GetType, which cannot see injected
    // types: it throws and skips the rest of the part. Take our blocks out first and feed them ourselves.
    [HarmonyPatch(typeof(Part), nameof(Part.ContructFromData))]
    public static class PartConstructPatch
    {
        static readonly string[] OurModules = { nameof(HydraulicCylinder), nameof(KalController), nameof(RoboticsTag) };
        static readonly Dictionary<IntPtr, List<(string module, List<string> lines)>> _pending = new Dictionary<IntPtr, List<(string, List<string>)>>();

        public static void Prefix(Part __instance, Il2CppSystem.Collections.Generic.List<string> data)
        {
            try
            {
                if (data == null) return;
                List<(string, List<string>)> found = null;
                for (int i = 0; i < data.Count; i++)
                {
                    string t = (data[i] ?? "").Trim();
                    if (!t.StartsWith("*")) continue;
                    string name = t.Substring(1).Trim();
                    if (Array.IndexOf(OurModules, name) < 0) continue;
                    // block = header line, "{", ..., matching "}"
                    int j = i + 1, depth = 0, start = -1;
                    var inner = new List<string>();
                    for (; j < data.Count; j++)
                    {
                        string s = (data[j] ?? "").Trim();
                        if (s == "{") { depth++; if (depth == 1) { start = j; continue; } }
                        else if (s == "}") { depth--; if (depth == 0) break; }
                        if (start < 0) break; // malformed: no opening brace
                        inner.Add(data[j]);
                    }
                    int end = Math.Min(j, data.Count - 1);
                    data.RemoveRange(i, end - i + 1);
                    (found ??= new List<(string, List<string>)>()).Add((name, inner));
                    i--;
                }
                if (found != null) _pending[__instance.Pointer] = found;
            }
            catch (Exception ex) { MelonLogger.Warning("[Robotics] load prefix: " + ex.Message); }
        }

        public static void Postfix(Part __instance, int version)
        {
            if (!_pending.TryGetValue(__instance.Pointer, out var found)) return;
            _pending.Remove(__instance.Pointer);
            foreach (var (module, lines) in found)
            {
                try
                {
                    var list = new Il2CppSystem.Collections.Generic.List<string>();
                    foreach (var l in lines) list.Add(l);
                    PartModule m;
                    if (module == nameof(RoboticsTag)) m = RoboticsTag.GetOrAdd(__instance);
                    else
                    {
                        m = module == nameof(HydraulicCylinder)
                            ? (PartModule)__instance.GetComponent<HydraulicCylinder>()
                            : __instance.GetComponent<KalController>();
                        if (m == null)
                        {
                            Robotics.Attach(__instance, Robotics.KindOf(__instance));
                            m = module == nameof(HydraulicCylinder) ? __instance.GetComponent<HydraulicCylinder>() : __instance.GetComponent<KalController>();
                        }
                    }
                    m?.ConstructFromData(list, version, true);
                }
                catch (Exception ex) { MelonLogger.Warning($"[Robotics] load {module}: {ex.Message}"); }
            }
        }
    }

    // ---------------------------------------------------------------- part icons
    // The mod loader ignores icon.png: load it ourselves when the game reads each part's config.
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LoadRef))]
    public static class ModManagerLoadRefPatch
    {
        public static void Postfix(string path, ModPartRef __result)
        {
            try
            {
                if (__result == null || __result.icon != null || Robotics.KindOf(path) == RoboticKind.None) return;
                string dir = System.IO.Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
                string file = System.IO.Path.Combine(dir, "icon.png");
                if (!System.IO.File.Exists(file)) return;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(file)))
                {
                    tex.name = "icon";
                    tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    __result.icon = tex;
                }
            }
            catch (Exception ex) { MelonLogger.Warning("[Robotics] icon: " + ex.Message); }
        }
    }

    // The part list shows PartRefItem.icon (a Sprite); the game only renders those for stock parts.
    public static class LibraryIcons
    {
        public static void Fix(PartRef pr)
        {
            try
            {
                if (pr == null || pr.availableParts == null) return;
                var arr = pr.availableParts;
                for (int i = 0; i < arr.Length; i++)
                {
                    // PartRefItem is a struct: change a copy, then write it back
                    var item = arr[i];
                    if (item == null || item.icon != null || item.modPartRef == null) continue;
                    var tex = item.modPartRef.icon;
                    if (tex == null || Robotics.KindOf(item.modPartRef.configPath) == RoboticKind.None) continue;
                    item.icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    arr[i] = item;
                }
            }
            catch (Exception ex) { MelonLogger.Warning("[Robotics] library icons: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PartRef), nameof(PartRef.Awake))]
    public static class PartRefAwakeIcons { public static void Postfix(PartRef __instance) => LibraryIcons.Fix(__instance); }

    [HarmonyPatch(typeof(PartRef), nameof(PartRef.StartPart))]
    public static class PartRefStartIcons { public static void Postfix(PartRef __instance) => LibraryIcons.Fix(__instance); }

    // ---------------------------------------------------------------- hangar preview safety
    // The cylinder preview moves attached parts; put them back before anything is serialized (save, autosave, launch).
    [HarmonyPatch(typeof(Part), nameof(Part.SerializeData))]
    public static class PartSerializePreviewReset
    {
        public static void Prefix() => HydraulicCylinder.ResetAllPreviews();
    }

    // ---------------------------------------------------------------- uniform scaling only
    // Free per-axis scaling would let you stretch the stroke without making the cylinder wider.
    public static class UniformScale
    {
        [ThreadStatic] static bool _busy;

        public static bool IsRobotic(Resizable r)
        {
            try { return r != null && (r.GetComponent<HydraulicCylinder>() != null || r.GetComponent<KalController>() != null); }
            catch { return false; }
        }

        /// <summary>Returns true when the call must be redirected with a uniform scale.</summary>
        public static bool Redirect(Resizable r, Vector3 s, out Vector3 uniform)
        {
            uniform = s;
            if (_busy || !IsRobotic(r)) return false;
            // Mirrored parts carry a negative axis (e.g. x = -1.4): compare sizes without the sign and
            // keep every axis' sign. No history: picking/placing makes new copies, so a remembered
            // value could be wrong.
            float ax = Mathf.Abs(s.x), ay = Mathf.Abs(s.y), az = Mathf.Abs(s.z);
            float hi = Mathf.Max(ax, Mathf.Max(ay, az)), lo = Mathf.Min(ax, Mathf.Min(ay, az));
            if (hi <= 1e-6f || hi - lo <= hi * 1e-4f) return false; // already uniform: leave it alone
            // the dragged axis is the odd one out (the other two are still equal)
            float v;
            if (Mathf.Abs(ay - az) <= hi * 1e-4f) v = ax;
            else if (Mathf.Abs(ax - az) <= hi * 1e-4f) v = ay;
            else if (Mathf.Abs(ax - ay) <= hi * 1e-4f) v = az;
            else v = ax + ay + az - hi - lo; // all differ: take the median
            uniform = new Vector3(Sign(s.x) * v, Sign(s.y) * v, Sign(s.z) * v);
            return true;
        }

        static float Sign(float f) => f < 0f ? -1f : 1f;

        public static void Call(Action a)
        {
            _busy = true;
            try { a(); } finally { _busy = false; }
        }
    }

    [HarmonyPatch(typeof(Resizable), nameof(Resizable.SetScale))]
    public static class ResizableSetScalePatch
    {
        public static bool Prefix(Resizable __instance, Vector3 newScale, bool setMirrored, bool fromLoad)
        {
            if (!UniformScale.Redirect(__instance, newScale, out var u)) return true;
            UniformScale.Call(() => __instance.SetScale(u, setMirrored, fromLoad));
            return false;
        }
    }

    [HarmonyPatch(typeof(Resizable), nameof(Resizable.SetScaleInternal))]
    public static class ResizableSetScaleInternalPatch
    {
        public static bool Prefix(Resizable __instance, Vector3 newScale, bool setMirrored, bool fromLoad)
        {
            if (!UniformScale.Redirect(__instance, newScale, out var u)) return true;
            UniformScale.Call(() => __instance.SetScaleInternal(u, setMirrored, fromLoad));
            return false;
        }
    }

    // ---------------------------------------------------------------- KAL-1000 output
    [HarmonyPatch(typeof(InputProcessor), nameof(InputProcessor.Process))]
    public static class InputProcessorProcessPatch
    {
        public static void Postfix(InputProcessor __instance)
        {
            if (Fields.TryGetOverride(__instance.Pointer, out float v)) __instance.value = v;
        }
    }
}
