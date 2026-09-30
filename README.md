# FF14 Remastered

An opt-in Dalamud customization plugin for FFXIV: combat and RP display profiles, an expandable job dock, native chat styling, fading chat history, local writing tools, and RP speech bubbles.

**Fresh installs retain FFXIV's native appearance.** The dock, minion menu, HUD spacing, borders, images, chat colours, fading, speech bubbles, presence markers, Mouse4 camera control, and automatic Direct Chat management begin disabled. Turn on the features you want in `/ff14remastered` or the plugin's configuration button.

The public name is **FF14 Remastered**. The internal assembly and configuration ID remains `CinematicMode` to preserve existing installations' settings, notes, and layouts. Install one copy only; existing users should replace the previous DLL rather than load both.

## Features

- **Display profiles:** select saved combat and RP HUD layouts automatically by monitor aspect ratio, with a combat override and a resize debounce. Manual profile commands are also available.
- **Job dock:** a vertical category menu opens horizontal rows of jobs. A click equips the job's saved gear set; changes are blocked during combat and other incompatible activities. An optional speaking-head button underneath opens all four RP tool rows.
- **Minion favourites:** a matching Minion Guide button opens three rows of twelve unlocked minions in Combat, RP, and RP minimal. In Combat it sits below Emotes; RP modes put it inside the left frame. Click to summon or dismiss using the native game action. The currently summoned favourite has a gold label. Favourites are editable in settings, saved separately for each character, and never contain duplicate or locked entries.
- **HUD spacing:** optionally arrange the three action bars, utility bar, HP/MP, EXP and status groups with clear gaps. The group clears the expanded RP chat area on the tested laptop layout, and popup menus keep space above chat. This option pauses in the native HUD editor and never reveals hidden bars.
- **RP minimal:** Right Ctrl in the RP profile toggles a framed, quieter view, preserves player nameplates and chat, and optionally enables Direct Chat only while that mode is active. On a combat profile, the key toggles cinematic mode.
- **Native chat colours:** stable speaker colours, pale message text, purple emotes and gold mentions. Native player and item links are retained.
- **Presence markers:** blue dots for online friends, green stars for online, red for known offline, grey for unknown, plus RP/Away/Busy labels. Message badges capture status at arrival; clicking one opens a list refreshed from currently available local game data. This is not a global online-status lookup.
- **Chat fading:** each new message fades in over 150 ms, remains fully readable for 20 seconds, then fades out over one second. Hovering chat or typing in its input reveals the history. Emptying the input or leaving the chat starts a two-second grace period before the history fades. The input, channel label, tabs and their buttons stay visible. The game's chat log is not deleted or rewritten by the fader.
- **Speech bubbles:** one recent nearby Say/Yell/Shout/custom-emote message per speaker in RP minimal, with a rounded pale face, gold outline and tail. Bubbles last 20 seconds, fading over the last three seconds. Drawing is bounded to 16 visible speakers within 60 yalms.
- **Four rows of RP tools:** unlocked quiet emotes, notes, post drafts, OOC formatting, private dice and scene prompts, walk toggle, the native emote list and Group Pose. Drafts are copied in UTF-8-safe chunks; they are never automatically sent.
- **Optional images:** attach local PNG backgrounds to native HUD elements. For deeper editing of individual native nodes, the settings can open the separately installed [HUDUnlimited](https://github.com/MidoriKami/HUDUnlimited) plugin.
- **Job-bar tools:** explicit preview/create/apply/verify commands and a portable generator for 43 job/class PvE layouts in the tested game data. Mouse4 can remain dedicated to camera look on every job.

Dice, scene prompts, previews and status responses use local chat output. Other players cannot see those logs. Motion-only emotes still animate your character for nearby players; writing tools require you to paste and send a post yourself.

## Compatibility and installation

Version 1.5.0 targets **Dalamud API 15 / .NET 10**, tested against Dalamud **15.0.3.6** on XIVLauncher under Linux/Wine. Native UI structures are game-version-dependent. The same source targets the Windows game API, but a separate Windows session has not been tested. This repository is a standalone community release, not a listing in the official Dalamud plugin repository.

1. Download the ZIP from [Releases](https://github.com/mhykolCS/ff14-remastered/releases) and extract it into a dedicated plugin directory.
2. Add the extracted `CinematicMode.dll` through Dalamud's **Dev Plugins** settings, then enable it in the plugin installer. Keep `KamiToolKit.dll` and the `assets` directory beside it.
3. Open `/ff14remastered` and enable your chosen options. Nothing applies a complete HUD preset automatically.

Existing development installs can point Dalamud at the newly built DLL. Keep the old internal ID and move the existing configuration only if you intentionally change your Dalamud configuration directory.

## Display profiles

First save your preferred native HUDs in slots 2 and 3 and retain a backup of your game configuration. In the plugin's `CinematicMode/display-hud.json`, a minimal setup that uses those existing layouts is:

```json
{
  "Enabled": true,
  "Mode": "auto",
  "LayoutsInitialized": true,
  "InitializeLayouts": false,
  "SetUpMonkHotbars": false,
  "SetUpTweaks": false,
  "CharacterId": 0,
  "CombatLayout": 2,
  "RpLayout": 3,
  "ExpandChat": false,
  "SwitchChatTab": false
}
```

The profile file reloads automatically. `ExpandChat` optionally resizes/repositions chat for each profile; `SwitchChatTab` selects the associated chat tab. Choose those explicitly if desired. `InitializeLayouts`, `SetUpMonkHotbars` and `SetUpTweaks` are legacy setup helpers that modify existing game or plugin settings and are disabled by default. They are not needed to switch between your saved layouts.

Commands:

| Command | Action |
| --- | --- |
| `/ff14remastered` or `/hudstudio` | Open customization settings |
| `/hudstudio reload` | Reload edited studio settings |
| `/displayhud auto`, `combat`, `rp`, `off`, `status` | Choose or inspect a display profile |
| `/rpminimal` or Right Ctrl | Toggle RP minimal in the RP profile |
| `/cinematic on`, `off`, `status` | Control cinematic/minimal mode |
| `/mousecamera on`, `off`, `status` | Use Mouse4 for hold-to-look on all jobs |
| `/jobdock on`, `off` | Toggle the job dock |
| `/rpdesk`, `/rpdesk draft` | Open local notes or a post draft |
| `/rppresence` | Open the current local presence list |
| `/hudclarity` | Explicitly set native 100% rendering and disable dynamic resolution |

## Gear sets and job hotbars

The dock selects an existing recommended gear set for a job, or falls back to another usable saved set. It does not acquire gear or create glamour plates.

The minion menu's initial selection mixes cosy companions, familiar characters and curious creatures. It takes inspiration from [community favourites](https://forum.square-enix.com/ffxiv/threads/491033), then filters against the current character's unlocked collection. It fills remaining slots from other owned minions; characters with fewer than 36 simply have fewer entries. The rest of the collection remains available in the native Minion Guide. Selecting a minion already in the panel swaps its two slots. No minions are purchased, unlocked or summoned automatically.

`/jobsetup preview` reports available jobs and sets. `/jobsetup create` explicitly creates recommended owned-equipment sets using the game's recommendation routine, retaining a saved original outfit and skipping unavailable jobs. Equipment changes should be reviewed in the game's gear-set list.

For the supplied keyboard/G502 layout, hotbar 1 uses `1 2 3 4 Q E R F Z X 5 Mouse5`, hotbar 2 adds Shift, and hotbar 3 adds Ctrl. Mouse4 is reserved for camera look when enabled. Set these keybinds in FFXIV's native keybind menu; the generator arranges hotbar slots, not global keyboard bindings.

The plugin exports a **private local** `job-catalog.json` from the current English game data. Generate and review a plan:

```sh
python3 tools/build_job_bars.py --catalog /path/to/job-catalog.json --output-dir /path/to/local-plan
```

Copy the reviewed `job-bars.json` into the plugin configuration directory, then explicitly run `/jobsetup applybars` and `/jobsetup verify`. These commands back up and validate the affected native slots. Bar 4 is preserved for shared utilities; PvP bars are not changed. Blue Mage follows its active spell book. The layouts include skills before unlock so their positions can remain consistent while leveling; use the plan as a starting point, not a rotation guide. The generator validates action names and IDs against the installed catalog and stops on mismatches after game patches.

## Build and checks

Install the .NET 10 SDK and supply the directory containing the installed API 15 `Dalamud.dll` and its host dependencies:

```sh
dotnet build FF14Remastered.csproj -c Release -p:DalamudPath=/path/to/dalamud/Hooks/dev
```

`DALAMUD_HOME` is also accepted. Without an override, the project checks the standard Windows or XIVLauncher Linux development-hook path. The project does not download or redistribute the game's or Dalamud's binaries.

The portable checks do not require the game:

```sh
dotnet run --project tests/CoreChecks -c Release
dotnet run --project tests/WritingChecks -c Release
dotnet run --project tests/PolicyChecks -c Release
dotnet run --project tests/MinionChecks -c Release
```

Additional native-payload checks require the host libraries:

```sh
dotnet run --project tests/ChatChecks -c Release -p:DalamudPath=/path/to/dalamud/Hooks/dev
```

Build before packaging:

```sh
python3 tools/package_release.py
```

The packager uses an explicit allowlist. Releases contain the plugin, its toolkit dependency, licensed icon and notices. They exclude runtime configuration, character IDs, notes, chat history, screenshots, credentials, game files, development backups and host DLLs.

## Local data and scope

The plugin stores its settings and private writing data in the normal Dalamud plugin configuration directory. Chat/presence/bubble text stays in memory; fading never exports the conversation. Local diagnostics and job setup backups can contain character or equipment data and should remain private. There is no telemetry or external chat transmission in this plugin.

Disabling chat fading or unloading the plugin restores the native transcript nodes. Disabling minimal mode restores the original camera settings and HUD visibility. Existing native HUD layout, gear-set and hotbar changes made through explicit setup commands remain saved by the game.

The game UI remains the foundation of this project. It does not independently reimplement every native combat element; custom overlays, native-node options, optional images, and HUDUnlimited provide the customization surface.

## License

Original source: [MIT](LICENSE). Bundled dependencies and artwork: [third-party notices](THIRD_PARTY_NOTICES.md).
