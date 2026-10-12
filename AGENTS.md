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
        → put/remove clothes (NpcInteractionManager methods)
        → kiss (NpcInteractionManager.KissHer or Emotions.NpcKiss)
        → modify_trust, modify_emotion, make_face, say
```

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
  (`new ChatMessage(false, dialogo)`) bypasses the filter.
- **Texture persistence** — All `Texture2D` created in `ChatStyles` must have `hideFlags = HideFlags.HideAndDontSave`.
- **Style reinitialization** — `ChatStyles.NeedsReinitialize()` is checked on every `Draw()` call to recover from scene changes that destroy texture references.
- **Window anchoring** — `ChatWindow.EnsureWindowPosition()` clamps the window to the top-right corner and prevents it from going off-screen.

---

## Adding New Commands

1. Add a new case to `CommandExecutor.ExecuteCommand()`.
2. Add a description and example to `PromptBuilder.BuildActionSection()` so the AI knows the command exists.
3. Add intent examples to `ActionIntentDetector` if you want natural-language triggering.
4. Update the command table in `README.md`.

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
| `NpcNavigatorAndRutines.cs` | Follow, routines, work state |
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
- [ ] After a **scene change**, the UI background stays opaque (not transparent).
- [ ] Buttons display **ASCII text** (no blank squares).
- [ ] Settings panel saves correctly and persists across restarts.
