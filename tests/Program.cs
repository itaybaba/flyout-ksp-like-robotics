using System;
using System.Linq;
using RoboticsMod;

int fails = 0, n = 0;
void Check(bool ok, string what) { n++; if (!ok) { fails++; Console.WriteLine("FAIL " + what); } }
bool Near(float a, float b, float eps = 1e-3f) => Math.Abs(a - b) < eps;

// --- curve evaluation
var t = new KalTrack { Min = 0, Max = 100 };
t.AddKey(0, 0); t.AddKey(2, 100); t.AddKey(4, 0);
Check(Near(t.Evaluate(0), 0) && Near(t.Evaluate(2), 100) && Near(t.Evaluate(4), 0), "keys hit exactly");
Check(Near(t.Evaluate(-1), 0) && Near(t.Evaluate(9), 0), "clamped outside keys");
Check(t.Evaluate(1) > 30 && t.Evaluate(1) < 70, "smooth midpoint between");
for (float x = 0; x <= 4; x += 0.01f) { var v = t.Evaluate(x); if (v < 0 || v > 100) { Check(false, "range clamp at " + x); break; } }
var lin = new KalTrack { Min = 0, Max = 10 };
lin.AddKey(0, 0, KeyMode.Linear); lin.AddKey(10, 10, KeyMode.Linear);
Check(Near(lin.Evaluate(2.5f), 2.5f), "linear interpolation");
var flat = new KalTrack { Min = -90, Max = 90 };
flat.AddKey(0, -90, KeyMode.Flat); flat.AddKey(1, 90, KeyMode.Flat);
Check(Near(flat.Evaluate(0.5f), 0f), "flat symmetric midpoint");
Check(flat.Evaluate(0.1f) < -80, "flat eases out");
var unsorted = new KalTrack { Min = 0, Max = 1 }; unsorted.AddKey(3, 1); unsorted.AddKey(1, 0);
Check(unsorted.Keys[0].Time == 1, "AddKey keeps order");
Check(Near(new KalTrack { Min = 5, Max = 9 }.Evaluate(1), 5), "empty track returns min");

// --- playback
var d = new KalData { Length = 2, SpeedPct = 100, Loop = LoopMode.Once };
d.TogglePlay(); for (int i = 0; i < 300; i++) d.Step(0.01f);
Check(!d.Playing && Near(d.Position, 2), "once stops at end");
d.TogglePlay(); Check(d.Playing && Near(d.Position, 0), "replay once restarts from start");
d.Direction = -1; d.Position = 1; d.Playing = true; for (int i = 0; i < 200; i++) d.Step(0.01f);
Check(!d.Playing && Near(d.Position, 0), "reverse once stops at 0");
var r = new KalData { Length = 2, Loop = LoopMode.Repeat, Playing = true };
for (int i = 0; i < 250; i++) r.Step(0.01f);
Check(Near(r.Position, 0.5f, 0.02f) && r.Playing, "repeat wraps");
var pp = new KalData { Length = 2, Loop = LoopMode.PingPong, Playing = true };
for (int i = 0; i < 250; i++) pp.Step(0.01f);
Check(Near(pp.Position, 1.5f, 0.02f) && pp.Direction == -1, "ping-pong bounces");
for (int i = 0; i < 200; i++) pp.Step(0.01f);
Check(pp.Direction == 1 && pp.Position >= 0, "ping-pong bounces back");
var sp = new KalData { Length = 10, SpeedPct = 50, Playing = true }; sp.Step(1);
Check(Near(sp.Position, 0.5f), "speed percent");
var big = new KalData { Length = 1, Loop = LoopMode.Repeat, Playing = true }; big.Step(3.25f);
Check(Near(big.Position, 0.25f), "repeat handles big dt");

// --- save / load round trip
var s = new KalData { Name = "Leg {walk}=1", Length = 7.5f, SpeedPct = 80, Loop = LoopMode.PingPong, Direction = -1, PlayOnLaunch = true, Enabled = false, KeyPlay = 15, KeyReverse = 16, KeyRestart = 0 };
var tr = new KalTrack { Tag = 123456789, PartId = 12, ProcessorIndex = 1, Title = "Extension|x", PartName = "Hydraulic Cylinder #12", Min = 0, Max = 100, Enabled = true, Symmetry = false };
tr.AddKey(0, 0); tr.AddKey(3.25f, 77.5f, KeyMode.Linear); tr.AddKey(7.5f, 10, KeyMode.Flat);
s.Tracks.Add(tr); s.Tracks.Add(new KalTrack { PartId = 3, Title = "Angle", Min = -90, Max = 90 });
var text = s.Serialize();
Check(!text.Contains("{") && !text.Contains("}"), "no braces in saved data (craft file blocks)");
var lines = text.Split('\n').Select(l => "  " + l + "  ").ToList(); // game may indent
var back = KalData.Parse(lines);
Check(back.Length == 7.5f && back.SpeedPct == 80 && back.Loop == LoopMode.PingPong && back.Direction == -1 && back.PlayOnLaunch && !back.Enabled, "settings round trip");
Check(back.KeyPlay == 15 && back.KeyReverse == 16 && back.KeyRestart == 0, "keys round trip");
Check(back.Tracks.Count == 2 && back.Tracks[0].Keys.Count == 3 && back.Tracks[1].Keys.Count == 0, "tracks round trip");
var b0 = back.Tracks[0];
Check(b0.PartId == 12 && b0.ProcessorIndex == 1 && !b0.Symmetry && b0.Max == 100 && b0.Tag == 123456789, "track fields round trip");
var legacy = KalData.Parse(new[] { "track=2|0|Extension|Cyl #2|0|100|1|1", "k=0,0,0" });
Check(legacy.Tracks.Count == 1 && legacy.Tracks[0].Tag == 0 && legacy.Tracks[0].PartId == 2, "v2.0 saves without tag still load");
Check(Near(b0.Keys[1].Value, 77.5f) && b0.Keys[1].Mode == KeyMode.Linear && b0.Keys[2].Mode == KeyMode.Flat, "keys values/modes round trip");
var junk = KalData.Parse(new[] { "length=abc", "k=1,2", "track=bad", "speed=50" });
Check(junk.SpeedPct == 50 && junk.Tracks.Count == 0, "malformed lines skipped");
var clone = s.Clone(); clone.Tracks[0].Keys[0].Value = 42;
Check(s.Tracks[0].Keys[0].Value == 0, "clone is deep");

Console.WriteLine($"{n - fails}/{n} checks passed");
return fails == 0 ? 0 : 1;
