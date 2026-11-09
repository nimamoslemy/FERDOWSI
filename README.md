<div align="center">

<img src="FERDOWSI.png" alt="FERDOWSI Logo" width="76" height="76" />

# FERDOWSI v2

### Above-Cursor AI Writing Assistant, Voice Dictation, Read Aloud & Bilingual Dictionary for Windows

<p>
  <a href="https://github.com/nimamoslemy/FERDOWSI/releases/tag/v2.0.0"><img src="https://img.shields.io/badge/Release-v2.0.0-10B981?style=for-the-badge" alt="Release v2.0.0" /></a>
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0F766E?style=for-the-badge&logo=windows&logoColor=white" alt="Windows 10/11 x64" />
  <img src="https://img.shields.io/badge/Core-C%2B%2B20%20%C2%B7%20C%23%20WPF%20%C2%B7%20Python%203-1E293B?style=for-the-badge" alt="C++20 C# Python" />
  <a href="LICENSE.txt"><img src="https://img.shields.io/badge/License-MIT-334155?style=for-the-badge" alt="MIT License" /></a>
</p>

<p>
  <a href="https://github.com/nimamoslemy/FERDOWSI/releases/download/v2.0.0/FERDOWSI_Installer_v2.0.0_x64.zip"><b>⬇ Download Installer ZIP (v2.0.0)</b></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/nimamoslemy/FERDOWSI/releases/download/v2.0.0/FERDOWSI_Portable_v2.0.0_x64.zip"><b>📦 Download Portable ZIP</b></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/nimamoslemy/FERDOWSI/releases/download/v2.0.0/FERDOWSI_Help_Guide.pdf"><b>📄 Help Guide (PDF)</b></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/nimamoslemy"><b>GitHub Profile</b></a>
</p>

</div>

---

## 📦 Download & Quick Install

To prevent browser SmartScreen warnings on raw `.exe` downloads, **FERDOWSI v2** is distributed as clean `.zip` packages:

| Package | Description | How to Run |
| :--- | :--- | :--- |
| **[`FERDOWSI_Installer_v2.0.0_x64.zip`](https://github.com/nimamoslemy/FERDOWSI/releases/download/v2.0.0/FERDOWSI_Installer_v2.0.0_x64.zip)** | **Recommended Setup Wizard** (Desktop & Start Menu shortcuts, optional Windows Startup tray launch, and uninstaller) | 1. Download & extract the `.zip` folder<br>2. Double-click **`Install_FERDOWSI.bat`** (or `FERDOWSI_Setup_v2.0.0.exe`) |
| **[`FERDOWSI_Portable_v2.0.0_x64.zip`](https://github.com/nimamoslemy/FERDOWSI/releases/download/v2.0.0/FERDOWSI_Portable_v2.0.0_x64.zip)** | **Zero-Install Portable Edition** | 1. Extract anywhere<br>2. Double-click **`Launch_FERDOWSI.bat`** (or `FERDOWSI.exe`) |
| **[`FERDOWSI_Help_Guide.pdf`](https://github.com/nimamoslemy/FERDOWSI/releases/download/v2.0.0/FERDOWSI_Help_Guide.pdf)** | **Bilingual User Manual (PDF)** | Full English & Persian guide with API key setup instructions |

---

## ⌨️ Global Windows Shortcuts

Use these system-wide shortcuts in any Windows application (Edge, Chrome, Word, Notepad, Telegram, VS Code, Outlook, etc.):

| Shortcut | Backup Shortcut | Action |
| :--- | :--- | :--- |
| **`Alt + G`** | `Ctrl + Shift + G` | **Summon / Toggle** the FERDOWSI card right above your cursor |
| **`Alt + V`** | `Ctrl + Shift + V` | **Start / Stop Voice Dictation (`🎤`)** into the active text box |
| **`Alt + R`** | `Ctrl + Shift + R` | **Read Aloud (`🔊`) / Stop Reading** selected or current text |
| **`Alt + Q`** | `Ctrl + Shift + F` | **Instant Quick-Fix (`Fix ✨`)** of the current sentence at cursor |
| **`Esc`** | — | Close the expanded card or stop active voice recording / reading aloud |

---

## ✨ Key Features in FERDOWSI v2

- **🎯 Streamlined Short Window Above Cursor**:
  - Clean left-to-right layout: **`[🎤] [🔊] [Red Error Count Badge] [Fix ✨] [×]`**.
  - Click **`×`** anytime to dismiss for the current box or pause for **10 minutes**, **30 minutes**, or **1 hour**.
  - Both **`🎤`** and **`🔊`** icons can be individually shown or hidden from **Main Settings**.
- **🎤 Voice Dictation (`Groq Whisper` & `Deepgram Nova-2`)**:
  - **Voice Output Modes in Main Settings**:
    - **`✨ Cleanup Voice`**: Removes spoken fillers (`um`, `uh`), stutters, and grammar errors while **strictly cleaning your speech — never answering questions or replying conversationally**.
    - **`🎙 Exact Voice`**: Types the exact spoken words verbatim without changes.
  - **Voice Languages in Main Settings**: Choose **English + Persian Mixed**, **Persian Only**, or **English Only**.
- **🔊 Neural Read Aloud (Text-to-Speech)**:
  - Read any selected or typed text aloud via **Google Gemini Audio TTS**, **Deepgram Aura**, **Voice AI**, or **Windows Offline SAPI**.
- **✍️ Custom Format & Reword Box (`✨ Reword`)**:
  - Rewrite text in **6 built-in tones** (`Fluent`, `Formal`, `Casual`, `Concise`, `Expand`, `Persuasive`), **12 smart formatters** (`Bullet List`, `Numbered List`, `Email Template`, `Finglish to Persian`, `Persian Half-Space`, `Title Case`, `snake_case`, `camelCase`), or **type your own custom format instruction** directly inside the `✨ Reword` tab and click **`Apply ✨`**.
- **📖 Bilingual AI Dictionary**:
  - Look up any word or sentence for Persian translations, part of speech, phonetic IPA, clickable **Synonyms (`Syn`)**, **Antonyms (`Ant`)**, and bilingual examples.
- **🔒 Automatic Password Protection & Bilingual Settings UI**:
  - Automatically detects password inputs across browsers and Windows apps, clears its typing buffer, and hides the widget immediately.
  - Switch the Settings interface language between **English** and **Persian** anytime with a single click.

---

## 🔑 How to Configure API Keys

Open **FERDOWSI v2 Settings** to configure your preferred providers (or select **`AI Only`** mode for `<0.8s` cloud inference):

1. **Google Gemini API** *(Free tier available)*: Click **`Get Gemini Key ↗`** ([aistudio.google.com/apikey](https://aistudio.google.com/apikey)), paste your key, and click **`⚡ Save & Test Google Gemini`**.
2. **Groq Whisper API** *(Fast voice dictation — Free tier available)*: Click **`Get Groq Key ↗`** ([console.groq.com/keys](https://console.groq.com/keys)), paste your `gsk_...` key, and click **`🎤 Save & Test Groq Dictation`**.
3. **Deepgram API** *(Voice dictation & Aura Read Aloud)*: Click **`Get Deepgram Key ↗`** ([console.deepgram.com](https://console.deepgram.com/)), paste your key, and click **`🎤 Save & Test Deepgram Voice`**.
4. **DeepSeek API & Voice AI API** *(Optional)*: Add a DeepSeek key ([platform.deepseek.com/api_keys](https://platform.deepseek.com/api_keys)) or Voice AI key in Settings.

> **Privacy & Security**: Your API keys are saved strictly inside your local Windows user profile (`%APPDATA%\FERDOWSI\settings.json`) and are never included in the repository or installer.

---

<div dir="rtl" align="right">

## 🇮🇷 راهنمای فارسی (FERDOWSI v2)

**فردوسی نسخه ۲ (`FERDOWSI v2`)** دستیار هوشمند نگارش، تایپ صوتی، بلندخوانی متن و دیکشنری دو زبانه است که در تمام برنامه‌های ویندوز دقیقاً بالای مکان‌نمای شما قرار می‌گیرد.

### 📥 دانلود و نصب آسان
برای جلوگیری از هشدار مرورگرها هنگام دانلود مستقیم فایل‌های `.exe`، نصاب برنامه داخل فایل فشرده **ZIP** قرار داده شده است:
1. فایل **[`FERDOWSI_Installer_v2.0.0_x64.zip`](https://github.com/nimamoslemy/FERDOWSI/releases/download/v2.0.0/FERDOWSI_Installer_v2.0.0_x64.zip)** را دانلود و از حالت فشرده خارج (Extract) کنید.
2. روی فایل **`Install_FERDOWSI.bat`** (یا `FERDOWSI_Setup_v2.0.0.exe`) دوبار کلیک کنید و دکمه **Install FERDOWSI** را بزنید.
3. برای مطالعه راهنمای کامل، فایل **[`FERDOWSI_Help_Guide.pdf`](https://github.com/nimamoslemy/FERDOWSI/releases/download/v2.0.0/FERDOWSI_Help_Guide.pdf)** را باز کنید یا در پنجره تنظیمات روی دکمه **«🌐 زبان: فارسی»** کلیک کنید تا محیط تنظیمات کاملاً فارسی شود.

### ✨ امکانات کلیدی نسخه ۲
- **اصلاح گرامر، املا، نیم‌فاصله و تبدیل فینگلیش به فارسی**: تبدیل خودکار جملاتی مانند `salam halet chetore` به `سلام، حالت چطوره؟` و اصلاح خطاهای نگارشی انگلیسی و فارسی.
- **تایپ صوتی هوشمند (`🎤`) در تنظیمات اصلی**: دارای دو حالت **`Cleanup Voice`** (حذف تپق‌ها و اصلاح نگارش، بدون اینکه هرگز به سوالات یا صحبت شما پاسخ مکالمه‌ای بدهد) و **`Exact Voice`** (تایپ دقیق کلمات گفته‌شده) با پشتیبانی از زبان‌های **فارسی، انگلیسی و ترکیبی (English + Persian Mixed)**.
- **بلندخوانی متن (`🔊 Read Aloud`)**: خواندن متن انتخاب‌شده با صدای طبیعی از طریق سرویس‌های Gemini، Deepgram و Voice AI.
- **فرمت و بازنویسی سفارشی در بخش `✨ Reword`**: علاوه بر ۶ لحن آماده، می‌توانید در کادر متنی بخش Reword دقیقاً فرمت یا دستور دلخواه خود را بنویسید و دکمه **`Apply ✨`** را بزنید.
- **میانبرهای سراسری ویندوز**:
  - **`Alt + G`**: باز و بسته کردن کارت فردوسی بالای مکان‌نما
  - **`Alt + V`**: شروع و پایان تایپ صوتی (`🎤`)
  - **`Alt + R`**: بلندخوانی متن (`🔊`)
  - **`Alt + Q`**: اصلاح فوری جمله فعلی (`Fix ✨`)

</div>

---

## 🛠️ Build from Source

```powershell
# 1. Compile C++20 native hook DLL + 64-bit C# WPF standalone executable
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 2. Package ZIP installer & portable archive
powershell -ExecutionPolicy Bypass -File .\package_installer.ps1

# 3. Run automated test suite & security audit
python .\tests\test_all.py
python .\tests\security_audit.py
```

---

<div align="center">

**GitHub**: [https://github.com/nimamoslemy](https://github.com/nimamoslemy)

</div>
