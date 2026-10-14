using System;
using Il2Cpp;
using Il2CppCraftEditor;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoboticsMod
{
    public class KalPanel : CustomPanel
    {
        public KalController kal;

        public override string Title => "KAL-1000 Controller";

        public KalPanel(IntPtr ptr) : base(ptr) { }
        public KalPanel() : base(ClassInjector.DerivedConstructorPointer<KalPanel>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        static readonly string[] LoopNames = { "Once", "Repeat", "Ping-Pong" };

        public override void InitializePanel()
        {
            if (kal == null) return;
            var d = kal.data;
            Header.Create("KAL-1000 Controller", "Plays keyframed tracks into any input of this craft: cylinders, hinges, control surfaces...", true);
            Header.Create($"{d.Tracks.Count} track(s)");

            HButton.Create("Open Track Editor", (Action<HButton>)(b => KalTrackEditor.Open(kal)));
            Toggle.Create("Enabled", d.Enabled, (Action<Toggle>)(t => d.Enabled = t.IsOn), "When disabled the controller leaves its inputs alone");
            Toggle.Create("Play on launch", d.PlayOnLaunch, (Action<Toggle>)(t => d.PlayOnLaunch = t.IsOn), "Start playing as soon as the flight starts");
            HButton.Create("Loop: " + LoopNames[(int)d.Loop], (Action<HButton>)(b =>
            {
                d.Loop = (LoopMode)(((int)d.Loop + 1) % 3);
                b.SetText("Loop: " + LoopNames[(int)d.Loop]);
            }));
            Slider.Create("Length (s)", d.Length, (Action<Slider>)(s =>
            {
                d.Length = Mathf.Max(0.5f, s.Float);
            }), 0.5f, 120f, "Length of the sequence in seconds", false, 0.5f);
            Slider.Create("Play speed %", d.SpeedPct, (Action<Slider>)(s => d.SpeedPct = s.Float), 0f, 200f, "Playback speed", true);

            Header.Create("Keys (flight)", "Click, then press a key");
            KeyButton("Play / pause", () => d.KeyPlay, v => d.KeyPlay = v);
            KeyButton("Reverse direction", () => d.KeyReverse, v => d.KeyReverse = v);
            KeyButton("Restart", () => d.KeyRestart, v => d.KeyRestart = v);
        }

        static void KeyButton(string title, Func<int> get, Action<int> set)
        {
            Header.Create(title);
            HButton.Create((Key)get(), (Action<Key>)(k => set(k == Key.Escape ? 0 : (int)k)));
        }

        public override void OnPanelClosed()
        {
            if (kal != null && !kal.isFlight && !KalTrackEditor.IsOpenFor(kal))
            {
                kal.data.Playing = false;
                kal.StopPreview();
            }
        }
    }
}
