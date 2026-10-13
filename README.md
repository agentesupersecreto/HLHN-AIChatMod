# HLHN AI Chat Mod

A BepInEx mod for **He Lives Here Now** by EvilGuest, that integrates AI-powered dialogue into the game. Press **F9** to open the chat window and hold real conversations with NPCs — the four main characters (Katherine, Susan, Stacy and Laura) and the secondary women of the neighbourhood. AI responses play through the game's native dialogue system and can trigger in-game actions.

---

## Features

- 🤖 **AI-Powered NPC Dialogue** — Chat with NPCs using Ollama (local), Gemini API, or Gemma.
- 🎭 **Native Game Integration** — AI responses appear in the game's speech bubbles via `Speach.Say()`.
- 🧠 **Character Context** — AI receives the NPC's full personality, trust, emotions, clothing status and available actions before responding.
- 🎮 **Action Commands** — AI can trigger native game actions embedded in its replies:
  - Follow / Stop following
  - Go to a place in the house (kitchen, sofa, bed, bathroom, outside...) or to the spots the routine
    knows by task (do the laundry, sweep the hallway, water the plants...)
  - Put/Remove any clothing item
  - Kiss, facial expressions
  - Trust & emotion modifications
- 💬 **Multilingual Intent Detection** — Commands are detected in English and any language the AI understands.
- 💾 **Persistent Memory** — Conversation history and per-character summaries are saved to disk.

---

## Requirements

| Requirement | Version |
|---|---|
| BepInEx | 5.x |
| .NET Standard | 2.1 |
| Game | HLHN v6.4+ |

---

## Installation

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) into the game folder.
2. Copy `HLHN_AIChatMod.dll` from `bin/Release/netstandard2.1/` to:
   ```
   <game_folder>/BepInEx/plugins/HLHN_AIChatMod.dll
   ```
3. Launch the game. The mod loads automatically.

---

## Configuration

On first run the mod creates its own folder:
```
<game_folder>/BepInEx/config/AIChat/
├── HLHN_AIChatMod.ini   <- settings (also editable from the Settings panel)
├── system_prompt.txt    <- editable system prompt template
├── custom_prompt.txt    <- extra rules injected into every prompt
├── locations.txt        <- optional extra places for the "go to" command
├── locations_dump.txt   <- written by [DEBUG] > Dump locations
├── animations_dump.txt  <- written by [DEBUG] > Dump animations
├── memory/              <- one file per character: persistent conversation summary, sexual milestones, orgasms, nudity, virginity
│   └── Katherine.txt
└── history/             <- one file per character: the saved chat log
    └── Katherine_history.txt
```

Everything the mod writes lives here, so the whole folder can be backed up or moved in one piece.

Upgrading from an older version? Those files are moved here automatically on startup from their
previous locations (`BepInEx/config/HLHN_AIChatMod.ini`, `BepInEx/AIchat/custom_prompt.txt`,
`LocalLow/<GameName>/AIchat/system_prompt.txt` and the per-character `Memory`/`History` folders under
`LocalLow/<GameName>/AIchat/`). A file that already exists here is never overwritten.

You can also configure directly in the **Settings panel** (press F9 → `[CONFIG]` button):

| Setting | Default | Description |
|---|---|---|
| Provider | `Ollama` | AI backend: `Ollama`, `Gemini`, `Gemma` |
| OllamaUrl | `http://localhost:11434/api/chat` | Ollama API endpoint |
| OllamaModel | `gemma3:4b` | Ollama model name |
| GemmaUrl | `http://localhost:11434/api/generate` | Gemma endpoint — must speak Ollama's `/api/generate` protocol (a bare host such as `http://localhost:11434` is completed automatically; LM Studio's OpenAI-style `/v1` is **not** supported by this client) |
| GemmaModel | `gemma3:4b` | Gemma model name |
| ApiKey | *(empty)* | Gemini API key |
| MaxHistory | `30` | Messages kept in context |
| AllowTrustMod | `true` | Allow AI to modify NPC trust |
| AI Commands | all `ON` | Per-command toggles (`follow`, `go to a place`, `clothes`, `kiss`, ...) stored in the `[Commands]` section of the `.ini` |

### Prompts: `system_prompt.txt` and `custom_prompt.txt`

Both live in `<game_folder>/BepInEx/config/AIChat/` and are created automatically on first run.

**`system_prompt.txt`** — the full system prompt template. Delete it and the mod regenerates the default one. These `{Placeholders}` are substituted on every request:

| Placeholder | Contents |
|---|---|
| `{CharacterContext}` | NPC name, role (main character of the house or secondary character of the neighbourhood), stats, trust thresholds, clothing state |
| `{Memory}` | Per-character long-term memory block: rolling conversation summary, sexual milestones (vaginal/anal/oral, virginity, orgasms, pregnancy), nudity, and intimacy facts |
| `{CustomRules}` | Your `custom_prompt.txt` text (as `[RULES] ...`) |
| `{Jailbreak}` | Local-model uncensoring line (empty for Gemini) |
| `{FormatInstruction}` | Plain-text `[CMD:]` mode or JSON mode, depending on the provider |
| `{AllowedCommands}` | The command list sent to the model |

**`custom_prompt.txt`** — appended to the prompt as an extra `[RULES]` block. Lines starting with `#` or `//` are comments and are ignored, so a fresh file adds nothing until you write something.

Both files are re-read whenever they change on disk: edit, save, and keep playing — no restart needed.

### Who lives in the house

The game has **four main characters** — Katherine, Susan, Stacy and Laura — and every other named woman is a
**secondary character** of the neighbourhood (the game flags them itself). The prompt tells the AI which
one the NPC in front of you is, so a secondary character will not claim the house, the routine or the
relationship with the player as her own.

A `system_prompt.txt` generated by an older version of the mod is automatically updated in place on startup
to reflect the four main characters while leaving any custom edits you made untouched.

---

## Usage

1. Approach an NPC in-game.
2. Press **F9** to open the AI Chat window.
3. Type your message and press **Enter** or click **Send**.
4. The AI responds — the response appears both in the chat window **and** as the NPC's speech bubble.
5. Press **F9** again (or click `[X]`) to close.

> The chat window title shows which NPC you are currently talking to.  
> Moving away from an NPC for 2+ seconds will clear the active character.

---

## AI Action Commands

The AI can embed special command tags in its responses to trigger game actions. These are processed automatically and removed from the displayed text.

### Command Format

```
[CMD:command_name]
[CMD:command_name:parameter]
```

### Available Commands

| Command | Example | Description | Trust requirement |
|---|---|---|---|
| `follow` | `[CMD:follow]` | NPC starts following the player | `Trust >= ValueToFollow` |
| `follow:stop` | `[CMD:follow:stop]` | NPC stops following | — |
| `goto:<place>` | `[CMD:goto:Do laundry]` | NPC walks to a place in the house and stays there doing what is done at that spot — a room (*kitchen*, *bathroom*...) or one of the house chores the routines know (*Do laundry*, *Sweeping Kitchen*, *Watering Living Plant*...) | `Trust >= ValueToFollow` |
| `places` | `[CMD:places]` | Lists the places the NPC can go to in this house | — |
| `remove_cloth:<slot>` | `[CMD:remove_cloth:dress]` | Remove a clothing item | `Trust >= ValueToUndress` |
| `put_cloth:<slot>` | `[CMD:put_cloth:bra]` | Put on a clothing item | — |
| `kiss` | `[CMD:kiss]` | NPC kisses the player | `Trust >= ValueToKiss` |
| `make_face:<type>` | `[CMD:make_face:happy]` | Change NPC facial expression | — |
| `modify_trust:<value>` | `[CMD:modify_trust:5]` | Increase/decrease trust | — |
| `modify_emotion:<stat>:<val>` | `[CMD:modify_emotion:submission:10]` | Modify an emotion stat | — |
| `say:<text>` | `[CMD:say:Come here]` | Force the NPC to speak the text in-game | — |

The thresholds are **enforced in code** (`CommandExecutor.CheckFollowThreshold()` / `CheckUndressThreshold()`, mirroring the game's own `NpcInteractionManager` checks), not only requested in the prompt: below the threshold the command returns a refusal feedback line and does nothing, and the reply still reaches the chat window.

### Sending an NPC somewhere (`goto`)

Ask in plain language — *"go to the kitchen"*, *"wait for me in the bedroom"*, *"sit on the sofa"*,
*"do the laundry"*, *"sweep the hallway"*, *"water the plants"*, *"go pray"*, *"call your friend"*,
*"vete a la cocina"*, *"haz la colada"* — and the NPC walks there on her own. The AI receives
the list of places the current NPC can reach and answers with `[CMD: goto:<place>]`; the mod then:

1. checks `Trust >= ValueToFollow` (the same requirement the game uses to let her follow you) and refuses
   in character if it is not met — no movement happens,
2. stops following and ends whatever chore she was busy with,
3. hands her a temporary chore for that spot and lets the **game's own routine system** walk her there,
   play the spot's animation (sit on the sofa, lie on the bed, cook...) and return to her routine when the
   visit is over.

The places come from the game itself — the first two sources are read from the running game, the third is
optional and hand-written:

| Source | What it is |
|---|---|
| Routine spots | The chores of *that* NPC's weekly routine (Monday..Sunday, specials, end-of-day). Real animation, objects and outfit. |
| Placed spots | `ChoreInPlace` spots placed in the scene, shared by every NPC. |
| `locations.txt` | Your own coordinates, for rooms the routines never visit. |

Optional stay: `goto:Do laundry:2` keeps her there for 2 in-game hours (default 1, capped so the visit
always fits before her end-of-day chore).

**Finding the places of your house.** The mod ships no room list — it asks the game. The names it offers
are the game's own, and since the house names most of its spots after what is done there, **every NPC has
her own list** (each one knows only the spots of her own routine). These are the real lists reported by
**Dump locations** on the house map:

```
Katherine (19): Broom upstair, Clean bathroom, Cleaning Kitchen Counter, Cook, Do laundry, Dust the
  living room, Dust upstair, Dusting Dinning Room, Kat End Day, Scrubbing Bathroom, Stuck Sink,
  Sweeping Dinner Room, Sweeping Hallway, Sweeping Kitchen, Sweeping Living Room, Washing Plates,
  Watering Kitchen Plant, Watering Living Plant, Watering Upstair Plant

Susan (14): Drinking Livingroom, Eating, Looking Basement, Looking Bedroom, Looking Dinner Room,
  Looking Downstair Bathroom, Looking Kitchen, Looking Living, Looking Outside, Looking Stacy,
  Looking Upstair, Looking Upstair Bathroom, Praying, Sleeping

Stacy (14): Eating, Hide Smoking, Listen Music Room, Listen Music Upstair, Masturbating In Her Room,
  Phone Bed Room, Phone Dinnerroom, Phone Living Room, Phone Upstair Living Room, Sleep, Smoking
  Outside, Stuck Window, Sunbathing, Watching TV
```

Asset names carry the owner's name (`SusanLookingKitchen`, `StacyPhoneBedRoom`); the prefix is dropped
because an NPC only ever lists her own spots, so the list reads *Looking Kitchen* rather than *Susan
Looking Kitchen*.

The AI gets that list on every turn and copies one name into `[CMD: goto:<place>]`; a request that equals
a name exactly goes straight to that place. Plain language works too, through two synonym tables
(English + Spanish, because the game mixes both in its asset names):

| What the player says | What it is matched against |
|---|---|
| **Rooms** — *kitchen*, *bathroom*, *bedroom*, *living room*, *dining room*, *hallway*, *upstairs*, *basement*, *outside*, *cocina*, *baño*... | the room part of a spot's name. The room weighs three times the chore, so "dust the dining room" resolves to a dining-room spot. Furniture is kept separate (*sofa*, *tv*), so "sit on the sofa" cannot land on the spot that watches TV |
| **Tasks** — *wash the dishes*, *do the laundry*, *sweep*, *dust*, *mop*/*scrub*/*wipe*, *vacuum*, *water the plants*, *cook*, *eat*, *sleep*, *pray*, *listen to music*, *use the phone*, *smoke*, *sunbathe*, *look around*, *drink*, *clean*, *lavar los platos*, *hacer la colada*, *barrer*, *regar*, *reza*, *fumar*... | the task part of a name (`WashingPlates`, `CheckingClothBasement`, `SweepingHallway`, `SusanPraying`, `ListenMusicRoom`, `StacyPhoneBedRoom`, `StacySmokingOutside`...). Washing up and the laundry are told apart even though the game calls both "washing" |

A chore the routine lists twice (asset name *and* description, e.g. `WashingPlates` / *Wash the plates*)
appears once, and asset names are spaced out for readability (`DustingUpstair` → *Dusting Upstair*). Press
F9 → `[DEBUG]` → **Dump locations** to write `locations_dump.txt`, which lists every place with its
coordinates, animation and keywords plus your own position — perfect for copying lines into
`locations.txt`:

```
# locations.txt  (BepInEx/config/AIChat/)
kitchen = 12.50,0.30,8.20
outside = 30.00,0.00,-12.00
sofa    = -3.20,0.30,4.75,Sit_Sofa
```

### Animations

Every character plays her poses from the clips inside her own Animator (`Animator.Play("Name")`), and the
routine system only ever plays the clip an entry carries in its *chore* (`Chore.AnimationName`). So there
are two useful lists per character:

* **routine animations** — the poses her weekly routine can reach. These are the same spots the `go to`
  command can send her to, so they are the ones to ask for;
* **animator clips** — every clip the character *can* play, read from her Animator controller. It is a
  superset: it includes her sex/pose clips and the shared ones (`Idle`, `Walk`, `Sit...`).

Press F9 → `[DEBUG]` → **Dump animations** to write `animations_dump.txt`: every character in the scene
(marked *main* or *secondary* following the game's own flag), with both lists.

For the full clip list of each character — read directly from the game's own asset files — see
[ANIMATIONS.md](ANIMATIONS.md): **293 clips** in the game, **183** for Katherine, **150** for Susan,
**175** for Stacy and **148** for Laura (plus the maid and the woman the game generates at random).

Shared poses appear in several routines (`SleepingCustom` in both Susan and Stacy, `Sit...`, `Idle`,
`Walk`), so a clip name alone does not identify a character; a secondary character generated by the game
usually has no routine at all, so her *routine* list comes back empty — that is normal, and is the same
reason "go to the kitchen" has nothing to match there.

### Clothing Slots

`dress`, `shirt`, `pants`, `skirt`, `bra`, `panty`, `socks`, `shoes`, `jacket`, `belt`, `glasses`, `gloves`, `hat`, `collar`, `all`

### Face Types
`happy`, `aroused`, `angry`, `annoyed`, `sad`, `neutral`

### Emotion Stats
`submission`, `perversion`, `desire`, `hornylevel`

---

## Project Structure

```
HLHN/
├── API/                    # AI client implementations (Ollama, Gemini, Gemma)
│   ├── GeminiClient.cs
│   ├── GemmaClient.cs
│   ├── OllamaClient.cs
│   ├── IApiClient.cs
│   └── PromptBuilder.cs
├── Core/                   # Mod core logic
│   ├── Plugin.cs               # BepInEx plugin entry point
│   ├── DialogBehaviour.cs      # Main MonoBehaviour, NPC tracking
│   ├── CharacterContextSnapshot.cs  # NPC data snapshot for AI
│   ├── ConversationMemory.cs   # Per-character persistent memory
│   └── SpamFilter.cs           # Deduplication of captured dialogue
├── Game/                   # Game integration
│   ├── CommandExecutor.cs      # Executes AI-embedded commands
│   ├── NpcGoToHandler.cs       # "go to a place" order (routine chore injection)
│   ├── LocationCatalog.cs      # Places read from the game + locations.txt
│   ├── PlaceMatcher.cs         # Place name/sentence resolution (pure logic)
│   ├── AnimationCatalog.cs     # Routine animations and animator clips discovery
│   ├── SentimentAnalyzer.cs    # Sentiment analysis for trust modification
│   ├── MovementHelper.cs       # Available places for the prompt
│   └── ActionIntentDetector.cs # Detects player intent from free text
├── Models/                 # Data models
│   └── ChatMessage.cs
├── Patching/               # Harmony patches
│   └── HarmonyPatches.cs
├── UI/                     # Chat window (Unity IMGUI)
│   ├── ChatWindow.cs
│   └── ChatStyles.cs
```

## Troubleshooting

| Symptom | Fix |
|---|---|
| Chat window doesn't open | Ensure BepInEx is installed correctly and the DLL is in `plugins/` |
| No speech bubble appears | Approach the NPC closer so `SpeachListener` detects them |
| AI returns empty responses | Check Ollama is running (`ollama serve`) or verify your Gemini API key |
| UI background disappears after scene change | Known Unity IMGUI issue; the mod auto-reinitializes styles on draw |
| "go to the kitchen" does nothing | Press F9 → `[DEBUG]` → **List places**. An empty list means this NPC has no routine spot in this scene; add your own place to `locations.txt` |
| The AI sends her to the wrong place | Place names are the game's own asset names. Check `locations_dump.txt` and add an explicit `name = x,y,z` line in `locations.txt` to pin that room |
| She never goes to a chore you ask for (*"go sweep the hallway"*) | Chore spots come from *that* NPC's routine. Press F9 → `[DEBUG]` → **List places** to see the chores she knows in this scene; for anything missing, add your own place to `locations.txt` (`hallway = -0.49,0.00,-7.70,Broom`) |

---

## License

For personal/modding use only. Not affiliated with the game developers.
