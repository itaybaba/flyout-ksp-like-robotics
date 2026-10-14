# KSP-Like Robotics v1

Hydraulic cylinders and a **KAL-1000** controller for **Flyout**, inspired by Kerbal Space Program's robotics parts. The cylinders physically push and pull whatever is attached to them. The KAL-1000 plays keyframed motion sequences on any input on your craft, including the game's own hinges.

[Download v1.0.0](https://github.com/itaybaba/flyout-ksp-like-robotics/releases/tag/v1.0.0) · [Report a problem](https://github.com/itaybaba/flyout-ksp-like-robotics/issues)

## Parts

All three parts are in the hangar's **ROB** (Robotics) tab.

| Part | What it does |
|---|---|
| **Hydraulic Cylinder** | Regular linear actuator. 0.85 m stroke, 60 kN at size 1. |
| **Telescopic Hydraulic Cylinder** | Three nested stages. 2.4 m stroke, 30 kN at size 1. |
| **KAL-1000 Controller** | Sequencer with keyframed tracks that drive any input on the craft. |

## Features

**Cylinders**

- Real physics, like the game's hinges. The cylinder's moving end and everything attached to it become their own body, sliding on a joint. Parts on top really move, and cylinders can be stacked on each other.
- Uniform scaling only. A bigger cylinder is longer and wider, extends farther and pushes harder (force grows with size²). Stretching a single axis scales the whole part instead.
- Panel settings: Size, Speed, Force limit, Min/Max extension, Invert, Hide model, and Preview extend.
- **Min extension** is the resting extension. The hangar shows the cylinder at that extension, with attached parts at the extended tip, and flight starts from there. Min and max can't cross: dragging one past the other pushes the other along.
- **Preview extend** (hangar only) slides the cylinder and the parts on its tip. Everything returns when the panel closes, and before saving or launching.
- **Hide model** hides only the cylinder's meshes. The part stays in place, keeps its physics and can still be selected.
- Control it with the normal **Input** section (the same key and axis bindings as hinges), or from a KAL-1000.

**KAL-1000**

- In the hangar, select the KAL and click **Open Track Editor**. In flight, right-click the KAL, or press **Ctrl+K** for the nearest one.
- **+ Add field** lists every input on the craft: cylinders, native hinges, control surfaces and more.
- Graph:
  - Double-click to add a point, and drag points to move them.
  - Click empty space to move the play head.
  - Type exact values in the Index, Time and Value boxes.
- Row tools: move up/down, copy/paste, flip, clear, delete. Point tools: add/remove, Smooth/Linear/Flat.
- Header: play/pause, stop, direction, jump to start/end, name, length, loop mode (**Once / Repeat / Ping-Pong**), play speed, Hide, close.
- KAL panel: Enabled, Play on launch, loop mode, length, speed, and keys for play/pause, reverse and restart.
- **Symmetry: on** also drives the mirrored partner of the controlled part.

## Requirements

- Windows x64 and Flyout. Tested game build: **0.2409**, Unity 2022.3.
- **MelonLoader 0.7.3**, installed for Flyout. Get the loader from [LavaGang's official releases](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3), and follow its [official installation guide](https://github.com/LavaGang/MelonWiki/blob/master/docs/gettingstarted.md), including its .NET runtime requirements.

## Optional automatic installer

If you prefer automatic folder detection, download **(optional)-KSP-Like-Robotics-v1-Installer.exe** from the Releases page. It's **optional**; the manual ZIP works too.

Close Flyout, run the installer, and click **Install mod**. It finds Flyout through Steam's registry entry and library folders, including libraries on other drives. If it finds several installations, choose the one you use. If detection fails, click **Browse** and select the folder containing `Flyout.exe`.

- **What it installs:** the mod DLL into `<Flyout>/Mods/` and the part files into `%USERPROFILE%\AppData\LocalLow\Stonext Games\Flyout\Mods\Parts\`. Everything is inside the installer, so it doesn't download anything.
- **What it doesn't install:** Steam, Flyout or MelonLoader. Install MelonLoader 0.7.3 first, using the official guide above.
- **Upgrades:** an old mod DLL is backed up under `<Flyout>/UserData/KSP-Like-Robotics-Backups/`.
- **Limits:** it won't install while Flyout is running. If your game folder is protected, you may need to run it as administrator.

The executable is unsigned and uses Windows' .NET Framework 4.x. You can always use the ZIP instead.

## Manual installation

1. Close Flyout.
2. In Steam, open Flyout → Manage → Browse local files. This is the folder containing `Flyout.exe`.
3. If you don't use MelonLoader yet, install it. Launch the game once to finish its setup, then close it.
4. Download **KSP-Like-Robotics-v1.zip** from this repository's Releases page. Extract its `Mods` folder into the Flyout folder, so the file ends up at `<Flyout>/Mods/RoboticsMod.dll`.
5. Start Flyout. On every start, the mod writes its part files (configs, meshes, icons) to `%USERPROFILE%\AppData\LocalLow\Stonext Games\Flyout\Mods\Parts\`. Open the hangar and look in the **ROB** tab.

Keep only one copy of `RoboticsMod.dll` in `Mods`.

## Saved crafts

Crafts store the cylinder and KAL settings inside the normal craft file. A craft that uses these parts needs the mod to load correctly. Without it, Flyout can't find the parts.

## Troubleshooting and uninstall

- **No ROB parts:** check `<Flyout>/MelonLoader/Latest.log` for `KSP-Like Robotics v1.0.0` and a line about the robotics part files. Restart the game once if this is the first launch after installing.
- **A cylinder doesn't move in flight:** look in the log for `[Robotics]` lines about the cylinder's joint. The cylinder needs to be attached to something by its base or its tip.
- **Reporting a bug:** include your game version, a description of the craft, the steps to reproduce it, and relevant parts of `Latest.log`. Review logs for personal information before sharing.
- **Uninstall:** close the game, delete `Mods/RoboticsMod.dll`, and delete the three `Robotics*` folders in `%USERPROFILE%\AppData\LocalLow\Stonext Games\Flyout\Mods\Parts\`.

## Validation

The release build has zero warnings and zero errors, and all **25 offline checks** of the KAL curve, playback and save logic pass. Live tests in Flyout 0.2409 covered cylinders moving attached parts, stacked cylinders, min/max extension, uniform scaling, save → reload → launch, KAL playback on cylinders and a native hinge, and a clean install from the DLL alone. See [validation details](docs/VALIDATION.md) for what hasn't been tested yet.

## Building from source

Install the .NET SDK with net6.0 support. Install Flyout with MelonLoader and launch it once, so the loader generates its local interop assemblies. Then, from the repository root:

```powershell
dotnet build RoboticsMod.csproj -c Release -p:FlyoutDir="C:/path/to/Flyout"
dotnet run -c Release --project tests/KalTests.csproj
```

The build uses the game and loader assemblies installed on your PC; those aren't redistributed. The `parts` folder is embedded into the DLL. Output: `bin/Release/net6.0/RoboticsMod.dll`.

## Building the optional installer

On Windows, build the mod in Release mode first, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/build.ps1
```

Output: `bin/installer/(optional)-KSP-Like-Robotics-v1-Installer.exe`. The build embeds your compiled DLL and the part files, using the Windows .NET Framework compiler. Two diagnostic modes write their report next to the executable:

- `--self-test` runs isolated fixture checks.
- `--detect-only` writes the detected game folders without installing anything.

## Credits and license

Created by **itaybaba**. Developed with AI assistance (Claude Code). The part meshes and icons are original, generated for this mod. No Kerbal Space Program assets are included.

This mod is inspired by KSP's Breaking Ground robotics. It isn't affiliated with Squad, Private Division, Take-Two or Stonext Games. Thanks to LavaGang for MelonLoader, the Harmony project, and the Il2CppInterop contributors. These dependencies keep their own licenses and are installed separately.

Original mod code: [MIT license](LICENSE.txt). Flyout and Kerbal Space Program names belong to their respective owners.
