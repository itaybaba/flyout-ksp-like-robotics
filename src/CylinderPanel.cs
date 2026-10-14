using System;
using Il2Cpp;
using Il2CppCraftEditor;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace RoboticsMod
{
    public class CylinderPanel : CustomPanel
    {
        public HydraulicCylinder cyl;
        Header _info;
        Slider _min, _max;
        bool _sync;

        public override string Title => cyl != null && cyl.telescopic ? "Telescopic Hydraulic Cylinder" : "Hydraulic Cylinder";

        public CylinderPanel(IntPtr ptr) : base(ptr) { }
        public CylinderPanel() : base(ClassInjector.DerivedConstructorPointer<CylinderPanel>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        string InfoText()
        {
            if (cyl == null) return "";
            return $"{cyl.Size:0.00}x  |  stroke {cyl.StrokeWorld:0.00} m  |  {cyl.MaxForce / 1000f:0} kN";
        }

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        void Mirror(Action<HydraulicCylinder> a)
        {
            a(cyl);
            try
            {
                var mp = cyl.part != null ? cyl.part.mirroredPart : null;
                var other = mp != null ? mp.GetComponent<HydraulicCylinder>() : null;
                if (other != null && other.Pointer != cyl.Pointer) a(other);
            }
            catch { }
        }

        public override void InitializePanel()
        {
            if (cyl == null) return;
            Header.Create(cyl.telescopic ? "Telescopic cylinder" : "Hydraulic cylinder",
                "Linear actuator. Bind keys/axes in the Input section below, or drive it from a KAL-1000 controller.", true);
            _info = Header.Create(InfoText());

            var res = cyl.GetComponent<Resizable>();
            if (res != null)
            {
                Slider.Create("Size", cyl.Size, (Action<Slider>)(s =>
                {
                    float v = Mathf.Clamp(s.Float, 0.25f, 4f);
                    var cur = res.targetGraphics != null ? res.targetGraphics.localScale : res.transform.localScale;
                    var sc = new Vector3(cur.x < 0 ? -v : v, cur.y < 0 ? -v : v, cur.z < 0 ? -v : v);
                    try { res.SetScale(sc, true, false); } catch (Exception e) { MelonLoader.MelonLogger.Warning("[Robotics] SetScale: " + e.Message); }
                    if (_info != null) _info.SetText(InfoText());
                }), 0.25f, 4f, "Uniform size: a bigger cylinder is longer AND wider, extends farther and pushes harder.", false, 0.05f);
            }

            Slider.Create("Speed", cyl.speedMul, (Action<Slider>)(s => Mirror(c => c.speedMul = s.Float)), 0.1f, 3f,
                "Extension speed multiplier", false, 0.05f);
            Slider.Create("Force limit %", cyl.forcePct, (Action<Slider>)(s =>
            {
                Mirror(c => c.forcePct = s.Float);
                if (_info != null) _info.SetText(InfoText());
            }), 1f, 100f, "Maximum push/pull force", true);
            // min can never pass max: dragging one past the other pushes the other along
            _min = Slider.Create("Min extension %", cyl.minPct, (Action<Slider>)(s =>
            {
                if (_sync) return;
                float v = s.Float;
                Mirror(c => { c.minPct = v; if (c.maxPct < v) c.maxPct = v; });
                if (_max != null && _max.Float < v) { _sync = true; try { _max.Float = v; } finally { _sync = false; } }
            }), 0f, 100f, "Extension when the input is at its minimum. The cylinder is built (and shown here) at this extension.", true);
            _max = Slider.Create("Max extension %", cyl.maxPct, (Action<Slider>)(s =>
            {
                if (_sync) return;
                float v = s.Float;
                Mirror(c => { c.maxPct = v; if (c.minPct > v) c.minPct = v; });
                if (_min != null && _min.Float > v) { _sync = true; try { _min.Float = v; } finally { _sync = false; } }
            }), 0f, 100f, "Extension when the input is at its maximum", true);
            Toggle.Create("Invert", cyl.invert, (Action<Toggle>)(t => Mirror(c => c.invert = t.IsOn)), "Swap extend and retract");
            Header.Create("View", "Visual only: hides the cylinder's mesh. The part, its physics, its collider and its selection stay in place.");
            HButton.Create(cyl.viewHidden ? "Show model" : "Hide model", (Action<HButton>)(b =>
            {
                bool h = !cyl.viewHidden;
                Mirror(c => c.SetViewHidden(h));
                b.SetText(h ? "Show model" : "Hide model");
            }));
            Slider.Create("Preview extend %", 0f, (Action<Slider>)(s => cyl.SetPreview(s.Float)), 0f, 100f,
                "Hangar preview only. Parts attached to the tip move along with it; everything snaps back when this panel closes and before saving or launching.", true, -1f, null, "V2", false, false, "V2", null, Slider.SliderType.Normal, false);
        }

        public override void UpdateInfo()
        {
            if (_info != null && cyl != null) _info.SetText(InfoText());
        }

        public override void OnPanelClosed()
        {
            if (cyl != null) cyl.SetPreview(-1f);
        }
    }
}
