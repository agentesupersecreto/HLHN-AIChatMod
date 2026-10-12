# AGENTS.md — AI Agent Guide for HLHN AI Chat Mod

This file provides instructions and context for AI agents (Copilot, Antigravity, etc.) working on this codebase.

---

## Project Overview

**HLHN AI Chat Mod** is a BepInEx mod for the Unity game *House of Love: Hot Neighbors*.  
It integrates an AI chat system into the game, allowing players to have natural conversations with NPCs. AI responses appear via the game's native `Speach.Say()` system and can trigger real in-game actions via embedded commands.

---

## Key Principles

1. **Use `FindFirstObjectByType<T>()`** — NOT the deprecated `FindObjectOfType<T>()`. The game runs Unity 6+.
2. **All NPC references are from `DialogBehaviour.Instance`** — `CurrentSpeach`, `CurrentEmotions`, `CurrentNavigator`, `CurrentInteractionManager`.
3. **`DontDestroyOnLoad`** is applied to the mod's `GameObject`. Styles must survive scene transitions (use `HideFlags.HideAndDontSave` on textures).
4. **Harmony patches should be minimal** — Only patch what's needed; prefer hooks via `DialogBehaviour` methods.
5. **AI response flow**: `PromptBuilder.Build()` → `IApiClient.SendMessage()` → `AIResponseProcessor.Process()` → `CommandExecutor.ExecuteCommand()` + `Speach.Say()`.
6. **English-only code, multilingual data**: identifiers, comments, log/status messages, UI labels and prompt text are English. The only non-English literals allowed are *input data* — intent keywords, cloth-slot synonyms and accent-folding tables (`ActionIntentDetector`, `CommandExecutor.ToCanonicalSlot`). Player input must work in any language: the model receives the raw message and the prompt's `LANG:` line makes it answer in that same language. Do **not** hardcode Spanish (or any other language) into the AI-facing protocol: the JSON keys and `[CMD:]` slots are English (`dialogue`/`style`/`command`, `remove_cloth:dress`).

---

## Architecture

```
Player Input (F9 / ChatWindow.cs)
    │
    ▼
DialogBehaviour.cs  (MonoBehaviour on DontDestroyOnLoad GameObject)
    ├── RefreshCurrentNpc()      → tracks active NPC via SpeachListener
    ├── CaptureCharacterContext() → CharacterContextSnapshot.Capture()
    └── ShowAIResponseInGame()   → Speach.Say() or ShowDialogue

PromptBuilder.cs
    └── Build(snapshot, history, memory, customPrompt)
        → Full system prompt with character personality, stats, clothing, available actions

IApiClient (OllamaClient / GeminiClient / GemmaClient)
    └── SendMessage(userMessage) : IEnumerator
        → calls PromptBuilder, sends HTTP request, parses response
        → calls AIResponseProcessor.Process()

AIResponseProcessor.cs
    └── Process(rawResponse)
        → extracts [CMD:...] tags → CommandExecutor.ExecuteCommand()
        → strips commands from display text
        → calls Speach.Say() for in-game speech
        → adds message to ChatHistory

CommandExecutor.cs
    └── ExecuteCommand(cmd)
        → follow / unfollow (NpcNavigatorAndRutines.MakeNpcFollowMe)
        → goto:<place> / places → NpcGoToHandler
        → put/remove clothes (NpcInteractionManager methods)
        → kiss (NpcInteractionManager.KissHer or Emotions.NpcKiss)
        → modify_trust, modify_emotion, make_face, say

Game/LocationCatalog.cs + Game/PlaceMatcher.cs
    └── What places exist in the current scene, and which one a player/AI name refers to
        → routine chores (Chore assets) + ChoreInPlace spots + locations.txt

Game/NpcGoToHandler.cs
    └── GoTo(place, hours) : string + Run() : IEnumerator
        → trust gate (CommandExecutor.CheckFollowThreshold)
        → stop following, end current chore
        → injects a temporary entry into CurrentDayChores
        → the game's own CheckForChores()/StartWorking() do the walking + animation
```

---

## Config & User Files

Everything the user can edit lives in **`BepInEx/config/AIChat/`**, created on first run:

| File | Purpose | Written by |
|---|---|---|
| `HLHN_AIChatMod.ini` | Settings | `ChatWindow.SaveConfig()` (created on first launch) |
| `system_prompt.txt` | Editable system prompt template with `{Placeholder}` substitution | `PromptBuilder.EnsurePromptFiles()` |
| `custom_prompt.txt` | Extra `[RULES]` text; `#` and `//` lines are comments | `PromptBuilder.EnsurePromptFiles()` |
| `locations.txt` | Optional extra places for the `goto` command (`name = x,y,z[,animation]`) | `LocationCatalog.EnsureLocationsFile()` |
| `locations_dump.txt` | Diagnostic dump of every place the game knows (written on demand from the `[DBG]` tab) | `LocationCatalog.Dump()` |

- **Never hardcode these paths** — always use `Plugin.ConfigDir` / `Plugin.GetConfigFilePath(Plugin.<X>Name)`, so a path change only touches `Plugin.cs`.
- `Plugin.MigrateFile()` imports files from older layouts on startup (`BepInEx/config/HLHN_AIChatMod.ini`, `BepInEx/AIchat/custom_prompt.txt`, `LocalLow/<Game>/AIchat/system_prompt.txt`).
- Both prompt files are picked up again when they change on disk, so users can edit them without restarting the game (`DialogBehaviour.GetCustomPrompt()` caches on last-write time).
- Per-character **memory/history is data, not config**: it stays under `Application.persistentDataPath/AIchat/` (`ConversationMemory`, `ChatWindow.SaveChatHistory`).

---

## NPC Tracking Logic

`DialogBehaviour.RefreshCurrentNpc()` is called every `Update()`:

- Finds `SpeachListener` in the scene.
- If `listener.NpcHit == null` for **2 seconds** (`NO_NPC_GRACE_SECONDS`), clears all current NPC references and saves memory.
- On NPC change: saves previous memory, clears `ChatHistory`, loads new character's memory.

> **Do not store persistent NPC references outside `DialogBehaviour`** — they will go stale on scene change or NPC deactivation.

---

## Cursor Management

- **Opening chat** (`SetVisible(true)`): `Cursor.visible = true`, `CursorLockMode.None`, `notMove.notmove = true`.
- **Closing chat** (`SetVisible(false)`): checks if other game menus are open via `NotMove.ActiveMenus` before hiding cursor.
- `LateUpdate()` and `OnGUI()` enforce cursor visible while chat is open (game may try to re-lock it).

The mod registers a dummy `GameObject` (`AIChat_MenuDummy`) with `NotMove.AddMenu()` so the game's own menu system correctly tracks the mod's open state.

---

## UI Notes (Unity IMGUI)

- **No emoji/Unicode** — Unity's default IMGUI font doesn't render them. Use ASCII (e.g. `[AI]`, `[X]`, `[SET]`).
  Legacy IMGUI text is BMP-only, so emoji outside the BMP (e.g. `📈` U+1F4C8) can never be drawn even with a custom dynamic font.
  System-message prefixes currently in use: `[EVT]` (game events), `[CMD]` (command feedback), `[+]` / `[-]` (trust up/down).
- **Never append to `ChatHistory` directly from Harmony patches** — route captured in-game events through
  `DialogBehaviour.AddCapturedEvent()` so the `SpamFilter` cooldown/dedup applies. Only the mod's own AI reply
  (`new ChatMessage(false, dialogue)`) bypasses the filter.
- **Texture persistence** — All `Texture2D` created in `ChatStyles` must have `hideFlags = HideFlags.HideAndDontSave`.
- **Style reinitialization** — `ChatStyles.NeedsReinitialize()` is checked on every `Draw()` call to recover from scene changes that destroy texture references.
- **Window anchoring** — `ChatWindow.EnsureWindowPosition()` clamps the window to the top-right corner and prevents it from going off-screen.

---

## Adding New Commands

1. Add a new case to `CommandExecutor.ExecuteCommand()`.
2. Add a description and example to `PromptBuilder.BuildCommandList()` so the AI knows the command exists.
3. Add intent examples to `ActionIntentDetector` if you want natural-language triggering.
4. Add a toggle to `DialogBehaviour` (`Cmd<X>Enabled`), to `ChatWindow.DrawSettings()` and to `SaveConfig()`/`LoadConfig()`.
5. Update the command table in `README.md`.

---

## Sending an NPC somewhere (`goto`)

The game has **no room/waypoint system**: NPC movement is driven by *chores* — `Chore` ScriptableObjects
(`AnimationName`, `Pos`, `Rot`) held by `NpcNavigatorAndRutines.Chores` entries with a time window,
optional `UseObject` and an outfit. `LocationCatalog` turns those (plus `ChoreInPlace` spots placed in the
scene, plus user-defined `locations.txt` entries) into a flat list of places, and `PlaceMatcher` resolves a
name or a sentence against the list using keyword groups (English + Spanish synonyms, because the game
mixes both languages in its asset names).

The house names most of its spots after the chore performed there (`WashingPlates`, `DustingLivingRoom`,
`CheckingClothBasement`, `SusanPraying`, `ListenMusicRoom`, `StacyPhoneBedRoom`...), which is why
`PlaceMatcher` keeps **two vocabularies**:

- `AreaSynonyms` — rooms and the furniture a player may name for them, **one concept per group**
  (`living`, `sofa` and `tv` are separate groups so that asking for one cannot drag in another spot of
  the same room; the bedroom group carries `bed` so that *"go to the bedroom"* reaches spots the game
  spells `BedRoom`). What she *does* there is a chore, never a room word, which is what keeps *"go to
  the bedroom"* (a room) apart from *"go to sleep"* (an activity).
- `ChoreSynonyms` — the housework the routine spots are named after. Washing up and the laundry are
  separate groups even though the game calls both "washing".

Scoring (`PlaceMatcher.ScoreRequest`): a request is read twice — as a whole (the AI copies a label, so a
label must match as-is) and as the destination extracted from it ("go to the kitchen" → "kitchen") — and
the better reading wins. Each reading scores `area * 3 + chore + general`, plus a bonus when a spot
matches both the room and the chore. `LocationCatalog.Resolve()` first returns a spot whose label equals
the query (normalized), then the best score, ties broken by distance to the NPC.

How a visit runs (`NpcGoToHandler`):

1. Refuse unless `Emotions.Trust >= Emotions.ValueToFollow` — the same rule the game uses in
   `NpcInteractionManager.NpcFollow()` / `NpcNavigatorAndRutines.Update()`.
2. `MakeNpcFollowMe()` if she is following (follow mode drives the agent every frame).
3. `EndChore(true)` if she is working — the game's own "finish this chore now".
4. Build a copy of the spot's routine entry (or a synthetic `Chore` for config spots) with
   `StartTime = now`, `EndTime = min(now + hours, CurrentEndChore.StartTime - 0.05)`, insert it at the
   head of `CurrentDayChores`.
5. `CheckForChores()` picks it up on the next frame and `StartWorking()` walks her there, plays the
   spot's animation and cleans everything up when the window ends (it removes the entry itself).

Rules that keep this safe:

- **Never mutate the routine's own chore entry** — always copy it; the routine must survive the detour.
- Candidate spots are validated with `NavMesh.SamplePosition()`: positions that are not on the current
  scene's NavMesh belong to another map (routines are stored per scene) and are dropped.
- Places are identified by **Chore asset + rounded position** (`PlaceKey`): the routine lists the same
  chore twice (asset name and `Desc`, e.g. `WashingPlates` / *Wash the plates*) and both entries would
  otherwise fill the AI's list; two *different* chores standing at the same spot stay separate (the game
  has a living-room and a dining-room dusting chore 16 cm apart).
- Labels are made readable with `PlaceMatcher.PrettyLabel()`: the routine's `Desc` when it has one,
  otherwise the asset name spaced out (`DustingUpstair` → *Dusting Upstair*) with the owner prefix
  dropped (`SusanLookingKitchen` → *Looking Kitchen*, `StacyPhoneBedRoom` → *Phone Bed Room*) — asset
  names are named after the NPC and an NPC only ever lists her own spots. The raw asset name stays in the
  spot's keywords, so both spellings keep matching.
- **Every NPC has her own spots** (Katherine's chores, Susan's *Looking…*/*Praying* spots, Stacy's
  *Phone…*/*Smoking*/*Listen Music* ones): the list is built per NPC from her own routine arrays, never
  shared between them.
- The list handed to the AI is sorted (config → routine → placed spot, then by name) and capped at
  `MAX_LISTED_SPOTS` (24): the AI can only send her somewhere whose name it was shown. Keep that list and
  the 700-character cap in `ActionIntentDetector.GetKnownPlaces()` in mind when adding sources.
- `CheckForChores()` needs `!Working`, `!PauseWork`, `!PlayingAnimation` and
  `ActualTime < CurrentEndChore.StartTime`; the handler waits for the first three and refuses the order
  when the day is over, instead of writing to game state from outside.
- Stopping what she is doing only happens through the public API (`EndChore`, `MakeNpcFollowMe`),
  never by driving `NavMeshAgent` by hand or by poking `PauseWork`, which other systems also own.

> **Trust gates are enforced in code, not only in the prompt.** The game gates its own actions in
> `NpcInteractionManager` (`NpcFollow()` → `ValueToFollow`, `KissHer()` → `ValueToKiss`,
> `RemoveClothes()` → `ValueToUndress`) but **not every path goes through them**: `MakeNpcFollowMe()`
> on `NpcNavigatorAndRutines` is a raw toggle, and `PutRemoveNoAnimation()` / `PutRemoveCloth()`
> (the path used while the NPC is working) skips the undress check entirely. `CommandExecutor`
> therefore mirrors those thresholds with `CheckFollowThreshold()` / `CheckUndressThreshold()` and
> returns a refusal string instead of acting. Do the same for any new action that has a threshold;
> returning `null` from a gate means "no stats available, keep the best-effort behaviour".

---

## Adding a New AI Provider

1. Implement `IApiClient` in a new file under `API/`.
2. Add the new provider to the `ProviderType` enum in `DialogBehaviour.cs`.
3. Add a case to `DialogBehaviour.RebuildApiClient()`.
4. Add a button for it in `ChatWindow.DrawSettings()`.

---

## Decompiled Source Reference

`Assembly-CSharp/` contains the decompiled game source (via dnSpy) for reference only.

Key files:
| File | Purpose |
|---|---|
| `Speach.cs` | Native speech bubble system (`Say(text)`) |
| `SpeachListener.cs` | Detects which NPC the player is looking at (`NpcHit`) |
| `Emotions.cs` | Trust, submission, perversion, desire, horny, facial expressions |
| `TalkInteractions.cs` | Dialogue tree data, personality traits |
| `NpcInteractionManager.cs` | All clothing slots and interaction methods |
| `NpcNavigatorAndRutines.cs` | Follow, routines, work state (`Chores`, `CheckForChores()`, `StartWorking()`, `EndChore()`) |
| `Chore.cs` | ScriptableObject with the position/rotation/animation of a routine spot |
| `ChoreInPlace.cs` | Scene-placed spot (trigger + `SexZone`) shared by every NPC |
| `NotMove.cs` | Cursor / input lock when menus are open |
| `ShowDialogue.cs` | Game UI for showing dialogue boxes |

---

## Testing Checklist

- [ ] Press F9 → chat window opens in **top-right corner**, doesn't clip off screen.
- [ ] Cursor becomes **visible and unlocked** when chat opens.
- [ ] Cursor becomes **hidden** when chat closes (if no other menus open).
- [ ] NPC name appears in title bar when close to an NPC.
- [ ] Title shows **"No NPC nearby"** after moving away for 2+ seconds.
- [ ] Typing a message and pressing Enter sends it.
- [ ] AI response appears in the **chat window** and as a **speech bubble**.
- [ ] Commands like "follow me", "remove your dress" work as expected.
- [ ] "go to the kitchen" makes her **walk** there and settle (animation of that spot); `[DBG]` shows
      `Places here` > 0 and the active `GoTo order`.
- [ ] `[DBG] → Dump locations` writes `locations_dump.txt` with the places and the player's position;
      a chore the routine lists twice (asset name + description) appears only once, with a readable name.
- [ ] "wash the dishes" / "do the laundry" / "sweep the hallway" / "vete a la cocina" each send her to
      the matching spot (the chore can be one of the routine's own, e.g. *Sweeping Hallway*).
- [ ] Below `ValueToFollow` the `goto` order is refused with a Trust message and she stays put.
- [ ] After a **scene change**, the UI background stays opaque (not transparent).
- [ ] Buttons display **ASCII text** (no blank squares).
- [ ] Settings panel saves correctly and persists across restarts.
