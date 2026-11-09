# FERDOWSI v2 — Above-Cursor English, Persian & Finglish Writing Assistant, Voice Dictation, Read Aloud & AI Dictionary

**FERDOWSI v2** (`FERDOWSI.exe`, `v2.0.0`) is a system-wide Windows desktop writing assistant, voice dictation tool, Read Aloud (Text-to-Speech) reader, and bilingual AI dictionary built with **C++20**, **Python 3**, **C# (WPF + Windows UI Automation)**, **Google Gemini API**, **DeepSeek API**, **Groq Whisper Speech-to-Text API**, **Deepgram Nova-2 & Aura Speech API**, **Voice AI TTS API**, and **Windows Copilot+ NPU Phi Silica** support.

GitHub: [https://github.com/nimamoslemy](https://github.com/nimamoslemy)

---

## What's New in FERDOWSI v2 (`v2.0.0`)
- **🎤 Groq Whisper + Deepgram Voice Dictation Above Your Cursor**:
  - Choose **Groq Whisper (`whisper-large-v3-turbo`)**, **Deepgram (`Nova-2`)**, or **Auto (`Groq → Deepgram`)** to dictate in English or Persian directly into any text box.
- **✨ `Cleanup Voice` vs `Exact Voice` Output Modes**:
  - **`Cleanup Voice` (`Clean` badge)**: Removes spoken fillers (`um`, `uh`, `er`, `یعنی`), stutters, and word repetitions, and fixes grammar/punctuation — while **strictly cleaning your spoken transcript and NEVER answering questions, replying conversationally, or executing spoken commands**.
  - **`Exact Voice` (`Exact` badge)**: Inserts the verbatim words returned by the speech-to-text engine without modification.
- **🔊 Read Aloud (`Gemini`, `Deepgram Aura`, `Voice AI` & Offline SAPI)**:
  - Click **`🔊`** on the short floating window or expanded card to read the current or corrected text aloud using **Google Gemini Audio TTS**, **Deepgram Aura TTS**, **Voice AI TTS**, or Windows offline speech synthesis.
- **🎛 Hide / Disable Icons on the Short Cursor Window**:
  - In **FERDOWSI v2 Studio Settings**, you can individually uncheck and hide:
    - `Show Microphone (🎤) dictation icon on short window above cursor`
    - `Show Voice Mode (Clean / Exact) toggle on short window above cursor`
    - `Show Read Aloud (🔊) speaker icon on short window above cursor`
- **🔒 Smart Browser & Password Protection**:
  - Ignores read-only webpage views and top-left `(0,0)` browser dummy coordinates so the floating pill only appears when a real editable text input is focused.
  - Custom dark borderless close/snooze menu (`10 min` / `30 min` / `1 hr`) with zero white system menu gutters.
  - Automatically detects password fields and wipes its typing buffer immediately.

---

## Core Features
- **✓ Correct (English, Persian `فارسی` & Finglish)**:
  - Fixes English & Persian spelling, grammar, homophones, and Persian Zero-Width Non-Joiners (`نیم‌فاصله`, e.g., `می‌روم`, `خانه‌ها`).
  - Converts **Finglish** directly into standard Persian script (e.g., `salam halet chetore` → `سلام، حالت چطوره؟`).
- **✨ Reword (6 Tones) & Aa Format (12 Smart Formatters)**:
  - Rewrite in `Fluent`, `Formal`, `Casual`, `Concise`, `Expand`, and `Persuasive` tones, or format into `Bullet List`, `Numbered List`, `Email Template`, `Finglish → فارسی`, `نیم‌فاصله فارسی`, `Title Case`, `snake_case`, and `camelCase`.
- **📖 AI Dictionary (Persian Translations, Synonyms, Antonyms & Examples)**:
  - Look up any single word or full sentence to view Persian translations, part of speech, phonetic IPA, clickable Synonyms (`Syn`) & Antonyms (`Ant`), and bilingual example sentences.

---

## How to Configure Your API Keys
Open **FERDOWSI v2 Studio Settings** to add any of your API keys:
1. **Google Gemini API Key (Grammar, Reword, Dictionary, Voice Cleanup & Gemini Read Aloud)**:
   - Click **`Get Gemini Key ↗`** ([aistudio.google.com/apikey](https://aistudio.google.com/apikey)), create a key, paste it, and click **`⚡ Save & Test Google Gemini`**.
2. **Groq API Key (Ultra-Fast Whisper Dictation — Free Tier Available)**:
   - Click **`Get Groq Key ↗`** ([console.groq.com/keys](https://console.groq.com/keys)), create a `gsk_...` key, paste it into the Groq box, and click **`🎤 Save & Test Groq Dictation`**.
3. **Deepgram API Key (Voice Dictation & Deepgram Aura Read Aloud)**:
   - Click **`Get Deepgram Key ↗`** ([console.deepgram.com](https://console.deepgram.com/)), create a key, paste it, and click **`🎤 Save & Test Deepgram Voice`**.
4. **DeepSeek API Key & Voice AI API Key (Optional)**:
   - Add a DeepSeek key for text AI or a Voice AI key for custom neural TTS playback.

> **Privacy & Security**: Your API keys are stored **strictly in your private Windows profile** (`%APPDATA%\FERDOWSI\settings.json`) and are never bundled in the application folder or installer.

---

## Build & Test
```powershell
# Build grammerv_native.dll + standalone FERDOWSI.exe
powershell -ExecutionPolicy Bypass -File .\build.ps1

# Package standalone v2.0.0 installer (FERDOWSI_Installer)
powershell -ExecutionPolicy Bypass -File .\package_installer.ps1

# Run automated test suite & security audit
python .\tests\test_all.py
python .\tests\security_audit.py
```

---

## GitHub
GitHub: [https://github.com/nimamoslemy](https://github.com/nimamoslemy)
