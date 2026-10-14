using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppCraftEditor;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using InputProcessor = Il2Cpp.InputProcessor;

namespace RoboticsMod
{
    /// <summary>KAL-1000: plays keyframed tracks into InputProcessors of the craft (like KSP Breaking Ground).</summary>
    public class KalController : PartModule
    {
        public KalData data = new KalData();
        public bool isFlight;

        // resolved processors per track (rebuilt when tracks change or parts disappear)
        readonly List<InputProcessor> _procs = new List<InputProcessor>();
        readonly List<Part> _parts = new List<Part>();
        int _resolvedVersion = -1;
        public int version;          // bump when tracks change
        float _nextResolve;

        public KalController(IntPtr ptr) : base(ptr) { }
        public KalController() : base(ClassInjector.DerivedConstructorPointer<KalController>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        public void TracksChanged() => version++;

        public void Resolve(bool force = false)
        {
            if (!force && _resolvedVersion == version && Time.time < _nextResolve) return;
            _procs.Clear(); _parts.Clear();
            foreach (var t in data.Tracks)
            {
                InputProcessor ip = null; Part p = null;
                try { ip = Fields.Resolve(part, t, out p); } catch { }
                _procs.Add(ip); _parts.Add(p);
                if (ip != null)
                {
                    try { t.Min = ip.min; t.Max = ip.max; } catch { }
                    t.PartName = Fields.PartLabel(p);
                    // tracks from older saves point at a part id: give the part a stable tag (hangar only, ids change on save)
                    if (t.Tag == 0 && !isFlight) t.Tag = RoboticsTag.Ensure(p);
                    if (p.id >= 0) t.PartId = p.id;
                }
            }
            _resolvedVersion = version;
            _nextResolve = Time.time + 2f; // parts can break off / be added, refresh now and then
        }

        public InputProcessor ProcessorOf(int track)
        {
            Resolve();
            return track >= 0 && track < _procs.Count ? _procs[track] : null;
        }

        public Part PartOf(int track)
        {
            Resolve();
            return track >= 0 && track < _parts.Count ? _parts[track] : null;
        }

        /// <summary>Write the current play position into every track's processor.</summary>
        public void Apply(bool preview)
        {
            Resolve();
            for (int i = 0; i < data.Tracks.Count && i < _procs.Count; i++)
            {
                var t = data.Tracks[i];
                if (!t.Enabled || t.Keys.Count == 0) continue;
                var ip = _procs[i];
                if (ip == null) continue;
                float v = t.Evaluate(data.Position);
                if (preview) { Preview(_parts[i], ip, v); if (t.Symmetry) PreviewMirror(_parts[i], v, ip); continue; }
                Fields.SetOverride(ip, v);
                if (t.Symmetry)
                {
                    try { var m = ip.Mirror; if (m != null && m.Pointer != ip.Pointer) Fields.SetOverride(m, v); } catch { }
                }
            }
        }

        static void Preview(Part p, InputProcessor ip, float v)
        {
            if (p == null) return;
            var cyl = p.GetComponent<HydraulicCylinder>();
            if (cyl == null) return;
            float span = ip.max - ip.min;
            float v01 = Mathf.Abs(span) < 1e-6f ? 0f : Mathf.Clamp01((v - ip.min) / span);
            if (cyl.invert) v01 = 1f - v01;
            cyl.SetPreview(Mathf.Lerp(cyl.minPct, cyl.maxPct, v01));
        }

        static void PreviewMirror(Part p, float v, InputProcessor ip)
        {
            try
            {
                var mp = p != null ? p.mirroredPart : null;
                if (mp == null) return;
                var mpart = mp.GetComponent<Part>();
                if (mpart != null && mpart.Pointer != p.Pointer) Preview(mpart, ip, v);
            }
            catch { }
        }

        public void StopPreview()
        {
            Resolve();
            foreach (var p in _parts)
            {
                if (p == null) continue;
                var cyl = p.GetComponent<HydraulicCylinder>();
                if (cyl != null) cyl.SetPreview(-1f);
                try { var mp = p.mirroredPart; var mc = mp != null ? mp.GetComponent<HydraulicCylinder>() : null; if (mc != null) mc.SetPreview(-1f); } catch { }
            }
        }

        // ------------------------------------------------------------------ lifecycle
        public override void InitializeModule()
        {
            isFlight = true;
            data.Position = data.Direction > 0 ? 0f : data.Length;
            data.Playing = data.PlayOnLaunch;
            Resolve(true);
            int ok = 0; foreach (var p in _procs) if (p != null) ok++;
            MelonLogger.Msg($"[Robotics] KAL-1000 #{part.id} '{data.Name}': {data.Tracks.Count} tracks, {ok} linked, autoplay={data.PlayOnLaunch}");
        }

        public override void UpdatePhysics()
        {
            if (!isFlight || !data.Enabled) return;
            data.Step(Time.fixedDeltaTime);
            Apply(false);
        }

        static bool Pressed(int key)
        {
            if (key <= 0) return false;
            try
            {
                var kb = Keyboard.current;
                return kb != null && kb[(Key)key].wasPressedThisFrame;
            }
            catch { return false; }
        }

        public override void Update()
        {
            if (isFlight)
            {
                if (KalTrackEditor.TextFocused) return;
                if (Pressed(data.KeyPlay)) data.TogglePlay();
                if (Pressed(data.KeyReverse)) data.Direction = -data.Direction;
                if (Pressed(data.KeyRestart)) { data.Position = data.Direction > 0 ? 0f : data.Length; data.Playing = true; }
            }
            else if (data.Playing)
            {
                // editor preview playback
                data.Step(Time.deltaTime);
                Apply(true);
            }
        }

        public override CustomPanel CustomPanel()
        {
            var p = new KalPanel();
            p.kal = this;
            return p;
        }

        public override void CopyMirror(PartModule module, bool fromMirror)
        {
            var o = module?.TryCast<KalController>();
            if (o != null) { data = o.data.Clone(); TracksChanged(); }
        }

        public override void OnPartDuplicated(PartModule source)
        {
            var o = source?.TryCast<KalController>();
            if (o != null) { data = o.data.Clone(); TracksChanged(); }
        }

        public override string SerializeData() => ModuleBlock.Wrap(nameof(KalController), data.Serialize());

        public override void ConstructFromData(Il2CppSystem.Collections.Generic.List<string> d, float ver, bool isMod = false)
        {
            if (d == null) return;
            var lines = new List<string>();
            for (int i = 0; i < d.Count; i++) lines.Add(d[i]);
            data = KalData.Parse(lines);
            TracksChanged();
        }

        public void OnDestroy()
        {
            if (KalTrackEditor.Current != null && KalTrackEditor.Current.Pointer == Pointer) KalTrackEditor.Close();
        }
    }
}
