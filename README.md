# HLHN AI Chat Mod

A BepInEx mod for **House of Love: Hot Neighbors** that integrates AI-powered dialogue into the game. Press **F9** to open the chat window and hold real conversations with NPCs (Laura, Susan, Stacy, Katherine) — AI responses play through the game's native dialogue system and can trigger in-game actions.

---

## Features

- 🤖 **AI-Powered NPC Dialogue** — Chat with NPCs using Ollama (local), Gemini API, or Gemma.
- 🎭 **Native Game Integration** — AI responses appear in the game's speech bubbles via `Speach.Say()`.
- 🧠 **Character Context** — AI receives the NPC's full personality, trust, emotions, clothing status and available actions before responding.
- 🎮 **Action Commands** — AI can trigger native game actions embedded in its replies:
  - Follow / Stop following
  - Put/Remove any clothing item
  - Kiss, facial expressions
  - Trust & emotion modifications
- 💬 **Multilingual Intent Detection** — Commands are detected in English and Spanish (and any language the AI understands).
- 💾 **Persistent Memory** — Conversation history and per-character summaries are saved to disk.
- 🪟 **Resizable UI** — Chat window anchors to the top-right corner, fully draggable, never leaves the screen.

---

## Requirements

| Requirement | Version |
|---|---|
| BepInEx | 5.x (Unity Mono) |
| .NET Standard | 2.1 |
| Unity | 2019+ |
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

On first run, a config file is created at:
```
%AppData%/../LocalLow/<GameName>/AIchat/config.txt
```

You can also configure directly in the **Settings panel** (press F9 → `[SET]` button):

| Setting | Default | Description |
|---|---|---|
| Provider | `Ollama` | AI backend: `Ollama`, `Gemini`, `Gemma` |
| OllamaUrl | `http://localhost:11434/api/chat` | Ollama API endpoint |
| OllamaModel | `gemma3:4b` | Ollama model name |
| GemmaUrl | `http://localhost:1234/v1` | Gemma/LM Studio endpoint |
| ApiKey | *(empty)* | Gemini API key |
| MaxHistory | `30` | Messages kept in context |
| AllowTrustMod | `true` | Allow AI to modify NPC trust |

### Custom System Prompt

Place a file at `<game_folder>/BepInEx/plugins/AIchat/custom_prompt.txt` to inject additional instructions into every AI prompt.

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

| Command | Example | Description |
|---|---|---|
| `follow` | `[CMD:follow]` | NPC starts following the player |
| `follow:stop` | `[CMD:follow:stop]` | NPC stops following |
| `remove_cloth:<slot>` | `[CMD:remove_cloth:dress]` | Remove a clothing item |
| `put_cloth:<slot>` | `[CMD:put_cloth:bra]` | Put on a clothing item |
| `kiss` | `[CMD:kiss]` | NPC kisses the player |
| `make_face:<type>` | `[CMD:make_face:happy]` | Change NPC facial expression |
| `modify_trust:<value>` | `[CMD:modify_trust:5]` | Increase/decrease trust |
| `modify_emotion:<stat>:<val>` | `[CMD:modify_emotion:submission:10]` | Modify an emotion stat |

### Clothing Slots

`dress`, `shirt`, `pants`, `skirt`, `bra`, `panty`, `socks`, `shoes`, `jacket`, `belt`, `glasses`, `gloves`, `hat`, `collar`, `all`

> Spanish equivalents are also accepted: `vestido`, `camisa`, `falda`, `sosten`, `calzones`, etc.

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
│   └── ActionIntentDetector.cs # Detects player intent from free text
├── Models/                 # Data models
│   └── ChatMessage.cs
├── Patching/               # Harmony patches
│   └── SpeachPatch.cs
├── UI/                     # Chat window (Unity IMGUI)
│   ├── ChatWindow.cs
│   └── ChatStyles.cs
└── Assembly-CSharp/        # Decompiled game source (reference only)
```

---

## Building

```bash
dotnet build HLHN_AIChatMod.csproj -c Release
```

Output: `bin/Release/netstandard2.1/HLHN_AIChatMod.dll`

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| Chat window doesn't open | Ensure BepInEx is installed correctly and the DLL is in `plugins/` |
| No speech bubble appears | Approach the NPC closer so `SpeachListener` detects them |
| AI returns empty responses | Check Ollama is running (`ollama serve`) or verify your Gemini API key |
| UI background disappears after scene change | Known Unity IMGUI issue; the mod auto-reinitializes styles on draw |
| Cursor stays visible after closing | Fixed in current version via `NotMove` + `Cursor.lockState` integration |

---

## License

For personal/modding use only. Not affiliated with the game developers.
