# Validation

Game build Flyout 0.2409, MelonLoader 0.7.3, Windows 11 x64.

## Build and offline checks

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `tests/KalTests.csproj`: 25/25 checks of KAL curve evaluation, loop modes, playback and save/load round trips.
- Installer `--self-test`: passed. It covered new and legacy Steam library formats, the missing-loader guard, fresh install, part files, upgrade with backup, payload integrity, and the wrong-folder guard.

## Live checks in game

- The mod loads and all five injected types register. The three parts appear once each in the ROB tab, with icons.
- **Clean install:** with the part folders deleted and only the DLL installed, the parts were written and loaded on the first launch.
- **Cylinders:** extend and retract physically in flight, and parts on the tip ride along.
- **Stacked cylinders:** a telescopic cylinder sitting on a cube that sits on another cylinder stays attached and follows it. Separation went 1.24 → 2.21 → 1.24 m.
- **Min extension:**
  - Min 50 % shows the cylinders half extended in the hangar, with tip parts moved.
  - The craft saves and launches at 50 %, input at minimum → 50 %, input at maximum → 100 %.
  - A cylinder with max 73 % stops at 73 %.
- **Scaling:** a non-uniform scale request (1, 1, 2) becomes a uniform 2×. Mirrored parts keep their mirror sign.
- **Save → reload → launch:** settings are preserved.
- **KAL-1000:**
  - Drives cylinders and a native hinge.
  - Ping-Pong and play on launch work.
  - The track editor renders in the hangar and in flight.
- **Hide model:** hides and shows the cylinder meshes.

## Not yet validated

- Mouse editing inside the track editor (adding and dragging points, typing values) has had only light manual testing.
- Cylinders mounted by their **tip** end (reversed mounting).
- Cylinders breaking off in crashes.
- Multiplayer, and game builds other than 0.2409.
