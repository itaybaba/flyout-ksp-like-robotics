using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppCraftEditor;
using UnityEngine;

namespace RoboticsMod
{
    /// <summary>
    /// "Axis fields" a KAL-1000 can control: every InputProcessor (the game's own input module used by
    /// hinges, control surfaces, our cylinders...) on parts of the same craft.
    /// </summary>
    public static class Fields
    {
        public class Entry
        {
            public Part Part;
            public InputProcessor Proc;
            public int Index;
            public string Title;
            public string PartName;
        }

        // processor pointer -> (value, Time.time it was written)
        static readonly Dictionary<IntPtr, (float v, float t)> _overrides = new Dictionary<IntPtr, (float, float)>();

        public static void SetOverride(InputProcessor p, float v)
        {
            if (p == null) return;
            _overrides[p.Pointer] = (v, Time.time);
        }

        /// <summary>Called from the InputProcessor.Process postfix.</summary>
        public static bool TryGetOverride(IntPtr p, out float v)
        {
            v = 0f;
            if (!_overrides.TryGetValue(p, out var o)) return false;
            if (Time.time - o.t > 0.25f) { _overrides.Remove(p); return false; }
            v = o.v;
            return true;
        }

        public static void ClearOverrides() => _overrides.Clear();

        public static bool InHangar => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "CraftEditor";

        public static Part Top(Part p)
        {
            int guard = 0;
            while (p != null && p.parentPart != null && guard++ < 10000) p = p.parentPart;
            return p;
        }

        public static string PartLabel(Part p)
        {
            if (p == null) return "?";
            string n = null;
            try { n = p.DisplayName; } catch { }
            if (string.IsNullOrEmpty(n)) n = p.partName;
            if (string.IsNullOrEmpty(n)) n = p.gameObject.name;
            return p.id >= 0 ? $"{n} #{p.id}" : n;
        }

        /// <summary>
        /// Parts of the craft the anchor belongs to. In the hangar part ids are -1 and parent links are empty
        /// until the craft is saved, so the editor's own craftParts list is used there.
        /// </summary>
        public static List<Part> CraftParts(Part anchor)
        {
            var list = new List<Part>();
            if (InHangar)
            {
                var ce = CEManager.instance;
                if (ce != null && ce.craftParts != null)
                {
                    foreach (var p in ce.craftParts) if (p != null) list.Add(p);
                    return list;
                }
            }
            var top = Top(anchor);
            foreach (var p in UnityEngine.Object.FindObjectsOfType<Part>())
            {
                if (p == null) continue;
                if (top != null)
                {
                    var t = Top(p);
                    if (t == null || t.Pointer != top.Pointer) continue;
                }
                list.Add(p);
            }
            return list;
        }

        public static List<InputProcessor> Processors(Part p)
        {
            var r = new List<InputProcessor>();
            if (p == null || p.modules == null) return r;
            foreach (var m in p.modules)
            {
                if (m == null) continue;
                var ip = m.TryCast<InputProcessor>();
                if (ip != null) r.Add(ip);
            }
            return r;
        }

        public static List<Entry> Available(Part anchor)
        {
            var r = new List<Entry>();
            var parts = CraftParts(anchor);
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var p = parts[pi];
                var procs = Processors(p);
                for (int i = 0; i < procs.Count; i++)
                {
                    string title = null;
                    try { title = procs[i].title; } catch { }
                    r.Add(new Entry { Part = p, Proc = procs[i], Index = i, Title = string.IsNullOrEmpty(title) ? "Input " + (i + 1) : title, PartName = PartLabel(p) });
                }
            }
            return r;
        }

        /// <summary>Find the processor a track points at: by its RoboticsTag, or by part id for older saves.</summary>
        public static InputProcessor Resolve(Part anchor, KalTrack t, out Part part)
        {
            part = null;
            var parts = CraftParts(anchor);
            Part found = null;
            if (t.Tag != 0)
                foreach (var p in parts) if (RoboticsTag.Get(p) == t.Tag) { found = p; break; }
            if (found == null && t.PartId >= 0)
                foreach (var p in parts) if (p.id == t.PartId) { found = p; break; }
            if (found == null) return null;
            var procs = Processors(found);
            if (t.ProcessorIndex < 0 || t.ProcessorIndex >= procs.Count) return null;
            part = found;
            return procs[t.ProcessorIndex];
        }
    }
}
