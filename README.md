# FERDOWSI v2 — Above-Cursor English, Persian & Finglish Writing Assistant, Voice Dictation, Read Aloud & AI Dictionary

**FERDOWSI v2** (`FERDOWSI.exe`, `v2.0.0`) is a system-wide Windows desktop writing assistant, voice dictation tool, Read Aloud (Text-to-Speech) reader, and bilingual AI dictionary built with **C++20**, **Python 3**, **C# (WPF + Windows UI Automation)**, **Google Gemini API**, **DeepSeek API**, **Groq Whisper Speech-to-Text API**, **Deepgram Nova-2 & Aura Speech API**, **Voice AI TTS API**, and **Windows Copilot+ NPU Phi Silica** support.

GitHub: [https://github.com/nimamoslemy](https://github.com/nimamoslemy)

---

## Download & Installation (Packaged as `.zip` — Zero Browser `.exe` Warnings)
- **`FERDOWSI_Installer_v2.0.0_x64.zip`**: Interactive Windows Setup Wizard package. Extract the ZIP and run **`Install_FERDOWSI.bat`** (or `FERDOWSI_Setup_v2.0.0.exe`).
- **`FERDOWSI_Portable_v2.0.0_x64.zip`**: Zero-install Portable Edition. Extract anywhere and run **`Launch_FERDOWSI.bat`** (or `FERDOWSI.exe`).
- **`FERDOWSI_Help_Guide.pdf`**: Bilingual (English & Persian) User Manual & API Setup Guide.

---

## Global Windows Shortcuts
- **`Alt + G`** (`Ctrl + Shift + G`): Summon / Toggle the FERDOWSI Card above your cursor
- **`Alt + V`** (`Ctrl + Shift + V`): Start / Stop **Voice Dictation (`🎤`)** anywhere in Windows
- **`Alt + R`** (`Ctrl + Shift + R`): **Read Aloud (`🔊`)** / Stop Reading selected or current text
- **`Alt + Q`** (`Ctrl + Shift + F`): **Instant Quick-Fix (`Fix ✨`)** of the current sentence at cursor
- **`Esc`**: Close the expanded card or stop active voice recording / reading aloud

---

## What's New in FERDOWSI v2 (`v2.0.0`)
- **🎤 Groq Whisper + Deepgram Voice Dictation (`[🎤] [🔊] [Red Error Count]` on Short Window)**:
  - Streamlined short cursor window with **`🎤`** (Microphone) and **`🔊`** (Read Aloud) placed to the left of the red error count badge, plus **`Fix ✨`** and **`×`** (10 min / 30 min / 1 hr pause menu).
- **✨ `Cleanup Voice` vs `Exact Voice` & Voice Language in Main Settings**:
  - Configure **`Cleanup Voice`** (cleans spoken fillers and grammar without ever answering or replying to your voice) vs **`Exact Voice`** (verbatim transcript), plus **Voice Language (`English + Persian Mixed`, `Persian / فارسی`, `English`)**, directly in **Main Settings**.
- **✍️ Custom Format / Reword Instruction Box in `✨ Reword`**:
  - Write any custom format or style instruction inside the **`✨ Reword`** tab (e.g., *"Rewrite as a polite email with 3 bullet points"*) and click **`Apply ✨`**.
- **🌐 Bilingual Settings UI (English & Persian `فارسی`) + PDF User Guide**:
  - Switch the Settings interface language between **English** and **Persian (`فارسی`)** anytime, and open **`FERDOWSI_Help_Guide.pdf`** for full bilingual documentation.

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
