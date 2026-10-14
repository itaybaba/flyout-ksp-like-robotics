using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoboticsMod
{
    // Pure KAL-1000 data + playback math. No Unity/game types so it can be tested offline.

    public enum KeyMode { Smooth = 0, Linear = 1, Flat = 2 }
    public enum LoopMode { Once = 0, Repeat = 1, PingPong = 2 }

    public class KalKey
    {
        public float Time, Value;
        public KeyMode Mode;
        public KalKey Clone() => new KalKey { Time = Time, Value = Value, Mode = Mode };
    }

    /// <summary>One row of the track editor: a curve driving one InputProcessor of one part.</summary>
    public class KalTrack
    {
        public int Tag;                  // RoboticsTag of the controlled part (stable identity)
        public int PartId = -1;          // Part.id of the controlled part (fallback for older saves)
        public int ProcessorIndex;       // n-th InputProcessor on that part
        public string Title = "";        // InputProcessor.title, for display + verification
        public string PartName = "";
        public float Min, Max = 1f;      // value range of the processor (graph Y range)
        public bool Enabled = true;
        public bool Symmetry = true;     // also drive the mirrored counterpart
        public List<KalKey> Keys = new List<KalKey>();

        public KalTrack Clone()
        {
            var t = (KalTrack)MemberwiseClone();
            t.Keys = new List<KalKey>();
            foreach (var k in Keys) t.Keys.Add(k.Clone());
            return t;
        }

        public void Sort() => Keys.Sort((a, b) => a.Time.CompareTo(b.Time));

        float Slope(int i)
        {
            var k = Keys[i];
            if (k.Mode == KeyMode.Flat || Keys.Count < 2) return 0f;
            if (k.Mode == KeyMode.Linear) return 0f; // linear is handled per segment
            // Smooth: Catmull-Rom style tangent from neighbours, flat at the ends (no overshoot at endpoints)
            if (i == 0 || i == Keys.Count - 1) return 0f;
            var a = Keys[i - 1]; var b = Keys[i + 1];
            float dt = b.Time - a.Time;
            return dt <= 1e-6f ? 0f : (b.Value - a.Value) / dt;
        }

        public float Evaluate(float t)
        {
            int n = Keys.Count;
            if (n == 0) return Min;
            if (t <= Keys[0].Time) return Keys[0].Value;
            if (t >= Keys[n - 1].Time) return Keys[n - 1].Value;
            int i = 0;
            while (i < n - 2 && t > Keys[i + 1].Time) i++;
            var k0 = Keys[i]; var k1 = Keys[i + 1];
            float dt = k1.Time - k0.Time;
            if (dt <= 1e-6f) return k1.Value;
            float u = (t - k0.Time) / dt;
            if (k0.Mode == KeyMode.Linear && k1.Mode == KeyMode.Linear)
                return k0.Value + (k1.Value - k0.Value) * u;
            float m0 = k0.Mode == KeyMode.Linear ? (k1.Value - k0.Value) / dt : Slope(i);
            float m1 = k1.Mode == KeyMode.Linear ? (k1.Value - k0.Value) / dt : Slope(i + 1);
            float u2 = u * u, u3 = u2 * u;
            float v = (2 * u3 - 3 * u2 + 1) * k0.Value + (u3 - 2 * u2 + u) * m0 * dt
                    + (-2 * u3 + 3 * u2) * k1.Value + (u3 - u2) * m1 * dt;
            // clamp overshoot to the processor range
            float lo = Math.Min(Min, Max), hi = Math.Max(Min, Max);
            return v < lo ? lo : v > hi ? hi : v;
        }

        public int AddKey(float time, float value, KeyMode mode = KeyMode.Smooth)
        {
            var k = new KalKey { Time = time, Value = value, Mode = mode };
            Keys.Add(k);
            Sort();
            return Keys.IndexOf(k);
        }
    }

    public class KalData
    {
        public float Length = 5f;           // seconds
        public float SpeedPct = 100f;       // play speed
        public LoopMode Loop = LoopMode.Once;
        public int Direction = 1;           // +1 forward, -1 reverse
        public bool PlayOnLaunch;
        public bool Enabled = true;
        public string Name = "KAL-1000 Controller";
        public int KeyPlay, KeyReverse, KeyRestart; // UnityEngine.InputSystem.Key values, 0 = none
        public List<KalTrack> Tracks = new List<KalTrack>();

        // runtime state (not saved)
        [NonSerialized] public float Position;
        [NonSerialized] public bool Playing;

        public KalData Clone()
        {
            var d = (KalData)MemberwiseClone();
            d.Tracks = new List<KalTrack>();
            foreach (var t in Tracks) d.Tracks.Add(t.Clone());
            return d;
        }

        /// <summary>Advance the play head. Returns true if it moved.</summary>
        public bool Step(float dt)
        {
            if (!Playing || Length <= 0f) return false;
            float before = Position;
            Position += dt * (SpeedPct / 100f) * Direction;
            switch (Loop)
            {
                case LoopMode.Once:
                    if (Position >= Length) { Position = Length; Playing = false; }
                    else if (Position <= 0f) { Position = 0f; Playing = false; }
                    break;
                case LoopMode.Repeat:
                    if (Position >= Length) Position -= Length * (float)Math.Floor(Position / Length);
                    else if (Position < 0f) Position += Length * (float)Math.Ceiling(-Position / Length);
                    break;
                case LoopMode.PingPong:
                    if (Position >= Length) { Position = 2 * Length - Position; Direction = -1; }
                    else if (Position <= 0f) { Position = -Position; Direction = 1; }
                    if (Position > Length) Position = Length;
                    if (Position < 0f) Position = 0f;
                    break;
            }
            return Position != before;
        }

        /// <summary>Play/pause. Starting a finished "Once" run restarts it from the correct end.</summary>
        public void TogglePlay()
        {
            if (Playing) { Playing = false; return; }
            if (Loop == LoopMode.Once)
            {
                if (Direction > 0 && Position >= Length) Position = 0f;
                if (Direction < 0 && Position <= 0f) Position = Length;
            }
            Playing = true;
        }

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        static string Esc(string s) => (s ?? "").Replace("|", "/").Replace("{", "(").Replace("}", ")").Replace("\n", " ").Replace("=", "-");

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("name=").Append(Esc(Name)).Append('\n');
            sb.Append("length=").Append(F(Length)).Append('\n');
            sb.Append("speed=").Append(F(SpeedPct)).Append('\n');
            sb.Append("loop=").Append((int)Loop).Append('\n');
            sb.Append("dir=").Append(Direction).Append('\n');
            sb.Append("autoplay=").Append(PlayOnLaunch ? 1 : 0).Append('\n');
            sb.Append("enabled=").Append(Enabled ? 1 : 0).Append('\n');
            sb.Append("keys=").Append(KeyPlay).Append(',').Append(KeyReverse).Append(',').Append(KeyRestart).Append('\n');
            foreach (var t in Tracks)
            {
                sb.Append("track=").Append(t.PartId).Append('|').Append(t.ProcessorIndex).Append('|').Append(Esc(t.Title))
                  .Append('|').Append(Esc(t.PartName)).Append('|').Append(F(t.Min)).Append('|').Append(F(t.Max))
                  .Append('|').Append(t.Enabled ? 1 : 0).Append('|').Append(t.Symmetry ? 1 : 0).Append('|').Append(t.Tag).Append('\n');
                foreach (var k in t.Keys)
                    sb.Append("k=").Append(F(k.Time)).Append(',').Append(F(k.Value)).Append(',').Append((int)k.Mode).Append('\n');
            }
            return sb.ToString();
        }

        public static KalData Parse(IEnumerable<string> lines)
        {
            var d = new KalData();
            KalTrack cur = null;
            foreach (var raw in lines)
            {
                if (raw == null) continue;
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                try
                {
                    switch (key)
                    {
                        case "name": d.Name = val; break;
                        case "length": d.Length = Math.Max(0.1f, P(val)); break;
                        case "speed": d.SpeedPct = P(val); break;
                        case "loop": d.Loop = (LoopMode)int.Parse(val); break;
                        case "dir": d.Direction = int.Parse(val) < 0 ? -1 : 1; break;
                        case "autoplay": d.PlayOnLaunch = val == "1"; break;
                        case "enabled": d.Enabled = val != "0"; break;
                        case "keys":
                            var ks = val.Split(',');
                            if (ks.Length >= 3) { d.KeyPlay = int.Parse(ks[0]); d.KeyReverse = int.Parse(ks[1]); d.KeyRestart = int.Parse(ks[2]); }
                            break;
                        case "track":
                            var p = val.Split('|');
                            cur = new KalTrack
                            {
                                PartId = int.Parse(p[0]), ProcessorIndex = int.Parse(p[1]), Title = p[2], PartName = p[3],
                                Min = P(p[4]), Max = P(p[5]), Enabled = p[6] == "1", Symmetry = p.Length < 8 || p[7] == "1",
                                Tag = p.Length > 8 ? int.Parse(p[8]) : 0
                            };
                            d.Tracks.Add(cur);
                            break;
                        case "k":
                            if (cur == null) break;
                            var kv = val.Split(',');
                            cur.Keys.Add(new KalKey { Time = P(kv[0]), Value = P(kv[1]), Mode = kv.Length > 2 ? (KeyMode)int.Parse(kv[2]) : KeyMode.Smooth });
                            break;
                    }
                }
                catch (Exception) { /* skip a malformed line, keep the rest of the controller */ }
            }
            foreach (var t in d.Tracks) t.Sort();
            return d;
        }
    }
}
