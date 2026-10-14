using System;
using System.Collections.Generic;
using System.Globalization;
using Il2Cpp;
using Il2CppCraftEditor;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

namespace RoboticsMod
{
    /// <summary>
    /// Linear hydraulic actuator. Works like the game's SwingJoint: RequiresMasterRB splits this part and
    /// everything attached after it into its own rigidbody, and a ConfigurableJoint slides it along +Z.
    /// </summary>
    public class HydraulicCylinder : PartModule
    {
        // ---- per-part-type constants (set by Setup) ----
        public bool telescopic;
        public float strokeLocal = 0.85f;      // extension at scale 1 (m)
        public float baseForce = 60000f;       // N at scale 1, force grows with size^2
        public float baseSpeed = 0.5f;         // m/s at scale 1, grows with size

        // ---- saved settings ----
        public float speedMul = 1f;            // 0.1 .. 3
        public float forcePct = 100f;
        public float minPct = 0f, maxPct = 100f;
        public bool invert;
        public float builtPct;                 // extension the craft is built at: tip parts and the tip connection sit there

        // ---- runtime ----
        public float ext01;                    // current extension 0..1 (measured in flight)
        public float target01;                 // ramped command 0..1
        public float previewPct = -1f;         // editor preview (-1 = off)
        public bool isFlight;
        public string status = "";

        InputProcessor _input;
        Transform _base, _rod, _tip, _tipCp;
        float _e0W;                            // built extension in world metres (flight)
        Transform[] _stages = Array.Empty<Transform>();
        readonly Dictionary<IntPtr, Vector3> _rest = new Dictionary<IntPtr, Vector3>();

        ConfigurableJoint _joint;
        Rigidbody _bodyA, _bodyB;
        Transform _parentT;
        Vector3 _p0InParent;                   // part origin (retracted) in parent-part space
        bool _reversed;                        // attached to the parent by the tip end
        readonly List<Transform> _reparented = new List<Transform>();
        int _jointTries;
        const int MaxJointTries = 300;          // physics steps (~6 s at 50 Hz)
        Quaternion _r0InParent = Quaternion.identity;
        bool _hasRest;
        float _strokeWorld, _forceMax;

        // ConfigurableJoint targetPosition is applied inverted in PhysX joint space.
        public static float DriveSign = -1f;

        public override bool RequiresMasterRB => true;

        public HydraulicCylinder(IntPtr ptr) : base(ptr) { }
        public HydraulicCylinder() : base(ClassInjector.DerivedConstructorPointer<HydraulicCylinder>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        public void Setup(bool tele)
        {
            telescopic = tele;
            strokeLocal = tele ? 2.4f : 0.85f;
            baseForce = tele ? 30000f : 60000f;
            baseSpeed = tele ? 0.6f : 0.5f;
        }

        // Resizable may scale the part root or only its graphics; the Base mesh sees both.
        public float Scale
        {
            get
            {
                if (_base == null) Cache();
                float s = _base != null ? _base.lossyScale.z : transform.lossyScale.z;
                return s <= 1e-4f ? 1f : s;
            }
        }
        public float Size => Scale;
        public float StrokeWorld => strokeLocal * Scale;
        public float MaxForce => baseForce * Size * Size * forcePct / 100f;

        // ------------------------------------------------------------------ graphics
        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        void Cache()
        {
            if (_tipCp == null)
            {
                try
                {
                    var cps = part != null ? part.connectionPoints : null;
                    if (cps != null && cps.Length > 1 && cps[1] != null) { _tipCp = cps[1].transform; Remember(_tipCp); }
                    else if (part != null)
                    {
                        // fall back to this part's own connection point that sits farthest along +Z
                        float best = 0.5f;
                        foreach (var c in GetComponentsInChildren<ConnectionPoint>(true))
                        {
                            if (c == null) continue;
                            var owner = c.Part();
                            if (owner == null || owner.Pointer != part.Pointer) continue;
                            float z = transform.InverseTransformPoint(c.transform.position).z / Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.z));
                            if (z > best) { best = z; _tipCp = c.transform; }
                        }
                        if (_tipCp != null) Remember(_tipCp);
                    }
                }
                catch { }
            }
            if (_base != null && _tip != null) return;
            _base = FindDeep(transform, "Base");
            _rod = FindDeep(transform, "Rod");
            _tip = FindDeep(transform, "Tip");
            var st = new List<Transform>();
            for (int k = 1; k <= 3; k++) { var s = FindDeep(transform, "Stage" + k); if (s != null) st.Add(s); }
            _stages = st.ToArray();
            foreach (var t in new[] { _base, _rod, _tip }) Remember(t);
            foreach (var t in _stages) Remember(t);
            _input = FindInput();
        }

        public string Dump()
        {
            Cache();
            string tipParts = "";
            if (!isFlight) foreach (var t in TipParts()) tipParts += $" {t.name}@{transform.InverseTransformPoint(t.position)}";
            string cpInfo = "?"; try { var cps = part != null ? part.connectionPoints : null; cpInfo = part == null ? "nopart" : cps == null ? "nocps" : cps.Length.ToString(); if (cps != null) foreach (var c in cps) cpInfo += c == null ? " null" : $" {c.name}@{transform.InverseTransformPoint(c.transform.position)}"; } catch (Exception ex) { cpInfo = ex.Message; }
            return $"cps[{cpInfo}] cyl #{part?.id} '{name}' min={minPct} max={maxPct} built={builtPct} rest={RestPct()} ext01={ext01} scale={Scale} " +
                   $"tip={(_tip != null ? transform.InverseTransformPoint(_tip.position).ToString() : "-")} cp={(_tipCp != null ? transform.InverseTransformPoint(_tipCp.position).ToString() : "-")} tipParts:{tipParts}";
        }

        float BuiltLocal => strokeLocal * Mathf.Clamp01(builtPct / 100f);

        /// <summary>Extension (%) the cylinder rests at: where its input sits by default, mapped through min/max.</summary>
        public float RestPct()
        {
            float v = 0f;
            if (_input != null)
            {
                try
                {
                    float span = _input.max - _input.min;
                    v = Mathf.Abs(span) < 1e-6f ? 0f : Mathf.Clamp01((_input.offset - _input.min) / span);
                }
                catch { }
            }
            if (invert) v = 1f - v;
            return Mathf.Clamp(Mathf.Lerp(minPct, maxPct, v), 0f, 100f);
        }

        // Mounted by the tip (the tip's part is our ancestor): the built pose stays retracted.
        bool TipIsParentInEditor()
        {
            try
            {
                var cps = part != null ? part.connectionPoints : null;
                var p = cps != null && cps.Length > 1 && cps[1] != null ? cps[1].GetConnectedPart() : null;
                return p != null && p.Pointer != part.Pointer && transform.IsChildOf(p.transform);
            }
            catch { return false; }
        }

        /// <summary>
        /// Hangar: keep the built pose at the rest extension (min extension). The tip, its connection point
        /// and everything attached to it sit at that extension, so it is saved that way and flight starts there.
        /// </summary>
        void SyncBuilt()
        {
            Cache();
            float want = TipIsParentInEditor() ? 0f : RestPct();
            if (Mathf.Abs(want - builtPct) > 0.01f)
            {
                bool wasPreview = previewPct >= 0f;
                if (wasPreview) RestoreMoved();
                float delta = strokeLocal * (Mathf.Clamp01(want / 100f) - Mathf.Clamp01(builtPct / 100f));
                Vector3 offset = transform.forward * (delta * Scale);
                foreach (var t in TipParts()) t.position += offset;
                builtPct = want;
                if (wasPreview) MoveTipParts(strokeLocal * Mathf.Clamp01(previewPct / 100f));
            }
            Place(_tipCp, BuiltLocal);
            if (previewPct < 0f) ApplyVisual(BuiltLocal, 0f);
        }

        void Remember(Transform t)
        {
            if (t != null && !_rest.ContainsKey(t.Pointer)) _rest[t.Pointer] = t.localPosition;
        }

        void Place(Transform t, float zOffsetLocal)
        {
            if (t == null || _reparented.Contains(t)) return;
            if (!_rest.TryGetValue(t.Pointer, out var r)) return;
            // graphics live under the part, possibly under an import root: convert part-space Z to the child's parent space
            var parent = t.parent;
            Vector3 restWorld = parent.TransformPoint(r);
            Vector3 world = restWorld + transform.forward * (zOffsetLocal * Scale);
            t.localPosition = parent.InverseTransformPoint(world);
        }

        /// <param name="e">extension in part-local metres (0..strokeLocal)</param>
        /// <param name="d">how far the part body itself is displaced along local Z (flight), 0 in the editor</param>
        void ApplyVisual(float e, float d)
        {
            Cache();
            float baseAbs = _reversed ? -e : 0f;
            float tipAbs = _reversed ? 0f : e;
            Place(_base, baseAbs - d);
            Place(_rod, tipAbs - d);
            Place(_tip, tipAbs - d);
            int n = _stages.Length;
            for (int k = 0; k < n; k++)
            {
                // stages share the stroke evenly; the last stage carries the tip
                float abs = baseAbs + (tipAbs - baseAbs) * (k + 1) / n;
                Place(_stages[k], abs - d);
            }
        }

        // ---- visual hide: only the meshes are switched off. The part, its physics, its colliders and its
        // selection stay where they are, so the cylinder can still be picked and placed.
        public bool viewHidden;

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        Renderer[] MeshRenderers()
        {
            Cache();
            var list = new List<Renderer>();
            AddRenderer(_base, list);
            AddRenderer(_rod, list);
            AddRenderer(_tip, list);
            foreach (var s in _stages) AddRenderer(s, list);
            return list.ToArray();
        }

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        static void AddRenderer(Transform t, List<Renderer> list)
        {
            if (t == null) return;
            var r = t.GetComponent<Renderer>();
            if (r != null) list.Add(r);
        }

        public int MeshRendererCount() => MeshRenderers().Length;

        public void SetViewHidden(bool hidden)
        {
            viewHidden = hidden;
            foreach (var r in MeshRenderers()) if (r != null) r.enabled = !hidden;
        }

        public void SetPreview(float pct)
        {
            previewPct = pct;
            if (isFlight) return;
            float e = pct < 0 ? BuiltLocal : strokeLocal * Mathf.Clamp01(pct / 100f);
            ApplyVisual(e, 0f);
            if (pct < 0f) { RestoreMoved(); _previewing.Remove(this); }
            else { MoveTipParts(e); if (!_previewing.Contains(this)) _previewing.Add(this); }
        }

        // ---- hangar preview: parts attached to the tip ride along, and are put back afterwards
        static readonly List<HydraulicCylinder> _previewing = new List<HydraulicCylinder>();
        readonly Dictionary<IntPtr, (Transform t, Vector3 applied)> _moved = new Dictionary<IntPtr, (Transform, Vector3)>();

        /// <summary>Undo every hangar preview (called before the craft is serialized for save/launch).</summary>
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        public static void ResetAllPreviews()
        {
            if (_previewing.Count == 0) return;
            foreach (var c in _previewing.ToArray())
            {
                try { if (c != null) c.SetPreview(-1f); } catch { }
            }
            _previewing.Clear();
        }

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        void AddTipCandidate(List<Transform> cands, Part p)
        {
            if (p == null || (part != null && p.Pointer == part.Pointer)) return;
            var t = p.transform;
            if (transform.IsChildOf(t)) return; // never drag our own parent/ancestors along
            foreach (var c in cands) if (c.Pointer == t.Pointer) return;
            cands.Add(t);
        }

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        List<Transform> TipParts()
        {
            var cands = new List<Transform>();
            try
            {
                var cps = part != null ? part.connectionPoints : null;
                if (cps != null && cps.Length > 1 && cps[1] != null) AddTipCandidate(cands, cps[1].GetConnectedPart());
            }
            catch { }
            // parts nested under this one and sitting on the upper half (surface-attached to the rod/tip)
            for (int i = 0; i < transform.childCount; i++)
            {
                var ch = transform.GetChild(i);
                var p = ch.GetComponent<Part>();
                if (p == null) continue;
                if (transform.InverseTransformPoint(ch.position).z / Mathf.Max(1e-4f, transform.lossyScale.z) > 0.55f) AddTipCandidate(cands, p);
            }
            // moving a parent already moves its nested children
            var result = new List<Transform>();
            foreach (var t in cands)
            {
                bool nested = false;
                foreach (var o in cands) if (o.Pointer != t.Pointer && t.IsChildOf(o)) { nested = true; break; }
                if (!nested) result.Add(t);
            }
            return result;
        }

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        void MoveTipParts(float eLocal)
        {
            // tip parts already sit at the built extension
            Vector3 offset = transform.forward * ((eLocal - BuiltLocal) * Scale);
            var keep = new HashSet<IntPtr>();
            foreach (var t in TipParts())
            {
                keep.Add(t.Pointer);
                _moved.TryGetValue(t.Pointer, out var m);
                t.position += offset - (m.t != null ? m.applied : Vector3.zero);
                _moved[t.Pointer] = (t, offset);
            }
            foreach (var k in new List<IntPtr>(_moved.Keys))
            {
                if (keep.Contains(k)) continue;
                var m = _moved[k];
                if (m.t != null) m.t.position -= m.applied;
                _moved.Remove(k);
            }
        }

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        void RestoreMoved()
        {
            foreach (var m in _moved.Values) if (m.t != null) m.t.position -= m.applied;
            _moved.Clear();
        }

        // ------------------------------------------------------------------ input
        public InputProcessor FindInput()
        {
            if (part == null || part.modules == null) return GetComponent<InputProcessor>();
            foreach (var m in part.modules)
            {
                if (m == null) continue;
                var ip = m.TryCast<InputProcessor>();
                if (ip != null) return ip;
            }
            return GetComponent<InputProcessor>();
        }

        float Desired01()
        {
            float v = 0f;
            if (_input != null)
            {
                // GetValue01 does not normalise by the processor range (raw value clamped to 0..1), so do it here
                try
                {
                    _input.Process();
                    float span = _input.max - _input.min;
                    v = Mathf.Abs(span) < 1e-6f ? 0f : Mathf.Clamp01((_input.value - _input.min) / span);
                }
                catch { }
            }
            if (invert) v = 1f - v;
            return Mathf.Clamp01(Mathf.Lerp(minPct, maxPct, v) / 100f);
        }

        // ------------------------------------------------------------------ lifecycle
        public override void AwakeModule() { Cache(); }
        public override void StartModule() { Cache(); }

        public override void InitializeModule()
        {
            isFlight = true;
            Cache();
            previewPct = -1f;
            ApplyVisual(BuiltLocal, 0f);
            target01 = Mathf.Clamp01(builtPct / 100f);
            // remember where this cylinder sits on its parent while everything is still at rest
            if (part != null && part.parentPart != null)
            {
                _parentT = part.parentPart.transform;
                _p0InParent = _parentT.InverseTransformPoint(transform.position);
                _r0InParent = Quaternion.Inverse(_parentT.rotation) * transform.rotation;
                _hasRest = true;
            }
            TryCreateJoint();
        }

        bool ParentConnectedByTip()
        {
            try
            {
                var cps = part.connectionPoints;
                if (cps == null || cps.Length < 2 || part.parentPart == null) return false;
                var tipCp = cps[1];
                var other = tipCp != null ? tipCp.GetConnectedPart() : null;
                return other != null && other.Pointer == part.parentPart.Pointer;
            }
            catch { return false; }
        }

        void TryCreateJoint()
        {
            // A cylinder mounted on another moving body only gets its own rigidbody a little after
            // InitializeModule, so keep retrying for a few seconds instead of giving up.
            if (_joint != null || _jointTries > MaxJointTries) return;
            _jointTries++;
            try
            {
                if (part == null || part.parentPart == null) { status = "no parent part"; return; }
                var myMaster = part.masterRB;
                var parentMaster = part.parentPart.masterRB;
                _bodyA = myMaster != null ? myMaster.rb : part.rb;
                _bodyB = parentMaster != null ? parentMaster.rb : part.parentPart.rb;
                if (_bodyA == null || _bodyB == null) { status = "waiting for rigidbodies"; return; }
                if (_bodyA.Pointer == _bodyB.Pointer)
                {
                    status = "waiting for own rigidbody";
                    if (_jointTries == MaxJointTries) MelonLogger.Warning($"[Robotics] cylinder #{part.id} never got its own rigidbody; it stays rigid");
                    return;
                }

                _reversed = ParentConnectedByTip();
                _parentT = part.parentPart.transform;
                if (!_hasRest)
                {
                    _p0InParent = _parentT.InverseTransformPoint(transform.position);
                    _r0InParent = Quaternion.Inverse(_parentT.rotation) * transform.rotation;
                    _hasRest = true;
                }
                else if (_jointTries > 1) SnapToRest();
                _strokeWorld = StrokeWorld;
                _forceMax = MaxForce;
                // built partly extended (min extension): the tip side was placed that far out in the hangar
                _e0W = _reversed ? 0f : _strokeWorld * Mathf.Clamp01(builtPct / 100f);
                target01 = _strokeWorld > 0f ? _e0W / _strokeWorld : 0f;

                Vector3 axisW = transform.forward * (_reversed ? -1f : 1f);
                Vector3 pW = transform.position;
                var aT = _bodyA.transform; var bT = _bodyB.transform;

                _joint = _bodyA.gameObject.AddComponent<ConfigurableJoint>();
                _joint.connectedBody = _bodyB;
                _joint.autoConfigureConnectedAnchor = false;
                _joint.axis = aT.InverseTransformDirection(axisW);
                _joint.secondaryAxis = aT.InverseTransformDirection(transform.up);
                _joint.anchor = aT.InverseTransformPoint(pW);
                _joint.connectedAnchor = bT.InverseTransformPoint(pW + axisW * (_strokeWorld * 0.5f - _e0W));
                _joint.xMotion = ConfigurableJointMotion.Limited;
                _joint.yMotion = ConfigurableJointMotion.Locked;
                _joint.zMotion = ConfigurableJointMotion.Locked;
                _joint.angularXMotion = ConfigurableJointMotion.Locked;
                _joint.angularYMotion = ConfigurableJointMotion.Locked;
                _joint.angularZMotion = ConfigurableJointMotion.Locked;
                _joint.enableCollision = false;
                _joint.projectionMode = JointProjectionMode.PositionAndRotation;
                _joint.projectionDistance = 0.02f;
                _joint.projectionAngle = 1f;
                var lim = new SoftJointLimit(); lim.limit = _strokeWorld * 0.5f;
                _joint.linearLimit = lim;
                SetDrive();
                _joint.targetPosition = new Vector3(DriveSign * (_e0W - _strokeWorld * 0.5f), 0f, 0f);

                // the fixed end stays with the parent body (like SwingJoint's fixed graphics)
                var fixedParts = _reversed ? new[] { _tip, _rod } : new[] { _base };
                foreach (var t in fixedParts)
                {
                    if (t == null) continue;
                    t.SetParent(_parentT, true);
                    _reparented.Add(t);
                }
                status = "ok";
                MelonLogger.Msg($"[Robotics] {(telescopic ? "telescopic" : "regular")} cylinder #{part.id} joint ready: stroke {_strokeWorld:F2} m, force {_forceMax / 1000f:F0} kN, moving mass {_bodyA.mass:F0} kg, reversed={_reversed}");
                MelonLogger.Msg($"[Robotics]   bodyA='{_bodyA.name}' kinematic={_bodyA.isKinematic} bodyB='{_bodyB.name}' parentPart='{part.parentPart.name}'(#{part.parentPart.id}) A-under-B={aT.IsChildOf(bT)} B-under-A={bT.IsChildOf(aT)} part-under-A={transform.IsChildOf(aT)} axisW={axisW} posW={pW}");
            }
            catch (Exception ex)
            {
                status = "joint error";
                MelonLogger.Warning("[Robotics] cylinder joint failed: " + ex);
                _jointTries = 999;
            }
        }

        /// <summary>
        /// The body was free for a few steps before its joint existed: put it back exactly where it sits
        /// on its parent (pose and velocity) so the joint starts from the built position.
        /// </summary>
        void SnapToRest()
        {
            Vector3 restPos = _parentT.TransformPoint(_p0InParent);
            Quaternion restRot = _parentT.rotation * _r0InParent;
            Quaternion delta = restRot * Quaternion.Inverse(transform.rotation);
            var aT = _bodyA.transform;
            Vector3 newPos = restPos + delta * (aT.position - transform.position);
            aT.SetPositionAndRotation(newPos, delta * aT.rotation);
            _bodyA.position = newPos;
            _bodyA.rotation = aT.rotation;
            _bodyA.velocity = _bodyB.GetPointVelocity(newPos);
            _bodyA.angularVelocity = _bodyB.angularVelocity;
            MelonLogger.Msg($"[Robotics] cylinder #{part.id} got its own rigidbody after {_jointTries} steps; snapped back to its built position");
        }

        void SetDrive()
        {
            if (_joint == null) return;
            float m = Mathf.Max(1f, _bodyA.mass);
            float k = _forceMax / Mathf.Max(0.005f, 0.02f * Scale);   // full force at 2 cm error (size 1)
            float c = 2f * Mathf.Sqrt(k * m);
            var drive = new JointDrive();
            drive.positionSpring = k;
            drive.positionDamper = c;
            drive.maximumForce = _forceMax;
            _joint.xDrive = drive;
        }

        float MeasuredExtensionWorld()
        {
            if (_parentT == null) return 0f;
            Vector3 p0 = _parentT.TransformPoint(_p0InParent);
            float e = Vector3.Dot(transform.position - p0, transform.forward);
            return _reversed ? -e : e;
        }

        public override void UpdatePhysics()
        {
            if (!isFlight) return;
            if (_joint == null) { TryCreateJoint(); if (_joint == null) return; }
            float dt = Time.fixedDeltaTime;
            float desired = Desired01();
            // speed grows with size, so a full stroke takes the same time at any size
            float rate = baseSpeed * speedMul / strokeLocal;
            float prev = target01;
            target01 = Mathf.MoveTowards(target01, desired, rate * dt);
            float vel = (target01 - prev) / Mathf.Max(1e-5f, dt) * _strokeWorld;
            _joint.targetPosition = new Vector3(DriveSign * (target01 * _strokeWorld - _strokeWorld * 0.5f), 0f, 0f);
            _joint.targetVelocity = new Vector3(DriveSign * vel, 0f, 0f);
        }

        public void LateUpdate()
        {
            if (!isFlight)
            {
                SyncBuilt();
                if (previewPct >= 0f)
                {
                    float e = strokeLocal * Mathf.Clamp01(previewPct / 100f);
                    ApplyVisual(e, 0f);
                    MoveTipParts(e);
                }
                return;
            }
            if (_joint == null) return;
            // the body has moved this far from where it was built; the built pose already had _e0W of extension
            float dW = MeasuredExtensionWorld();
            float eW = Mathf.Clamp(dW + _e0W, 0f, _strokeWorld);
            ext01 = _strokeWorld > 0 ? eW / _strokeWorld : 0f;
            float dL = (eW - _e0W) / Scale;
            ApplyVisual(eW / Scale, _reversed ? -dL : dL);
        }

        public override void OnDisconnectModule(RootPart newRoot) { RestoreGraphics(); }

        public void OnDestroy()
        {
            RestoreMoved();
            _previewing.Remove(this);
            // fixed-end graphics were moved under the parent part; take them with us
            foreach (var t in _reparented) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            _reparented.Clear();
        }

        void RestoreGraphics()
        {
            foreach (var t in _reparented) if (t != null) t.SetParent(transform, true);
            _reparented.Clear();
            if (_joint != null) { UnityEngine.Object.Destroy(_joint); _joint = null; }
        }

        // ------------------------------------------------------------------ editor
        public override CustomPanel CustomPanel()
        {
            var p = new CylinderPanel();
            p.cyl = this;
            return p;
        }

        public override void OnPartScaled(Vector3 newScale)
        {
            if (previewPct >= 0f) SetPreview(previewPct);
        }

        public void CopySettings(HydraulicCylinder o)
        {
            speedMul = o.speedMul; forcePct = o.forcePct; minPct = o.minPct; maxPct = o.maxPct; invert = o.invert;
            // a clone copies the source's transforms, so it is already built at the source's extension:
            // take over that state and the source's rest positions (ours were captured already moved)
            builtPct = o.builtPct;
            Cache(); o.Cache();
            CopyRest(o._base, _base, o); CopyRest(o._rod, _rod, o); CopyRest(o._tip, _tip, o); CopyRest(o._tipCp, _tipCp, o);
            for (int k = 0; k < _stages.Length && k < o._stages.Length; k++) CopyRest(o._stages[k], _stages[k], o);
        }

        void CopyRest(Transform from, Transform to, HydraulicCylinder o)
        {
            if (from != null && to != null && o._rest.TryGetValue(from.Pointer, out var r)) _rest[to.Pointer] = r;
        }

        public override void CopyMirror(PartModule module, bool fromMirror)
        {
            var o = module?.TryCast<HydraulicCylinder>();
            if (o != null) CopySettings(o);
        }

        public override void OnPartDuplicated(PartModule source)
        {
            var o = source?.TryCast<HydraulicCylinder>();
            if (o != null) CopySettings(o);
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // The game writes module text verbatim: each module emits its own "*Name { ... }" block.
        public override string SerializeData()
        {
            return ModuleBlock.Wrap(nameof(HydraulicCylinder),
                $"speed={F(speedMul)}\nforce={F(forcePct)}\nmin={F(minPct)}\nmax={F(maxPct)}\ninvert={(invert ? 1 : 0)}\nbuilt={F(builtPct)}");
        }

        public override void ConstructFromData(Il2CppSystem.Collections.Generic.List<string> data, float version, bool isMod = false)
        {
            if (data == null) return;
            for (int i = 0; i < data.Count; i++)
            {
                var line = (data[i] ?? "").Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq);
                if (!float.TryParse(line.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) continue;
                switch (k)
                {
                    case "speed": speedMul = Mathf.Clamp(v, 0.05f, 5f); break;
                    case "force": forcePct = Mathf.Clamp(v, 1f, 100f); break;
                    case "min": minPct = Mathf.Clamp(v, 0f, 100f); break;
                    case "max": maxPct = Mathf.Clamp(v, 0f, 100f); break;
                    case "invert": invert = v > 0.5f; break;
                    case "built": builtPct = Mathf.Clamp(v, 0f, 100f); break;
                }
            }
        }
    }
}
