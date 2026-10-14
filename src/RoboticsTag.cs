using System;
using System.Globalization;
using Il2Cpp;
using Il2CppInterop.Runtime.Injection;

namespace RoboticsMod
{
    /// <summary>
    /// Invisible module that gives a part a stable identity for KAL-1000 tracks.
    /// Part.id is -1 in the hangar until the craft is saved, so it can't be used as a reference there.
    /// Added only to parts a KAL-1000 controls; saved as "*RoboticsTag { tag=N }".
    /// </summary>
    public class RoboticsTag : PartModule
    {
        public int robotId;

        public RoboticsTag(IntPtr ptr) : base(ptr) { }
        public RoboticsTag() : base(ClassInjector.DerivedConstructorPointer<RoboticsTag>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        static readonly Random _rng = new Random();

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        public static int Get(Part p)
        {
            if (p == null) return 0;
            var t = p.GetComponent<RoboticsTag>();
            return t != null ? t.robotId : 0;
        }

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        public static RoboticsTag GetOrAdd(Part p)
        {
            var t = p.GetComponent<RoboticsTag>();
            if (t == null)
            {
                t = p.gameObject.AddComponent<RoboticsTag>();
                t.part = p;
            }
            Robotics.EnsureModule(p, t);
            return t;
        }

        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
        public static int Ensure(Part p)
        {
            if (p == null) return 0;
            var t = GetOrAdd(p);
            if (t.robotId == 0) t.robotId = _rng.Next(1, int.MaxValue);
            return t.robotId;
        }

        // copies must not share the identity of the original
        public override void OnPartDuplicated(PartModule source) { robotId = 0; }
        public override void OnMirrorCreated(PartModule mirrorModule)
        {
            var m = mirrorModule?.TryCast<RoboticsTag>();
            if (m != null && m.Pointer != Pointer) m.robotId = 0;
        }

        public override string SerializeData() => robotId == 0 ? "" : ModuleBlock.Wrap(nameof(RoboticsTag), "tag=" + robotId.ToString(CultureInfo.InvariantCulture));

        public override void ConstructFromData(Il2CppSystem.Collections.Generic.List<string> data, float version, bool isMod = false)
        {
            if (data == null) return;
            for (int i = 0; i < data.Count; i++)
            {
                var l = (data[i] ?? "").Trim();
                if (l.StartsWith("tag=") && int.TryParse(l.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) robotId = v;
            }
        }
    }
}
