using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GrammerV
{
    public class GrammerVSettings
    {
        public string GoogleApiKey { get; set; }
        public string GoogleModel { get; set; }
        public string DeepSeekApiKey { get; set; }
        public string DeepSeekModel { get; set; }
        public string DeepgramApiKey { get; set; }
        public string DeepgramLanguage { get; set; }
        public string GroqApiKey { get; set; }
        public string GroqModel { get; set; }
        public string SttProvider { get; set; } // "auto", "groq", "deepgram"
        public string VoiceMode { get; set; } // "cleanup", "exact"
        public string VoiceAiApiKey { get; set; }
        public string TtsProvider { get; set; } // "auto", "gemini", "deepgram", "voice_ai", "windows_sapi"
        public string AiProvider { get; set; } // "ai_only", "auto", "google", "deepseek", "local", "offline"
        public bool FloatingEnabled { get; set; }
        public bool ShowOnTypingAndFocus { get; set; }
        public bool ShowMicOnShortWindow { get; set; }
        public bool ShowVoiceModeOnShortWindow { get; set; }
        public bool ShowReadAloudOnShortWindow { get; set; }

        public GrammerVSettings()
        {
            GoogleApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                           ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                           ?? string.Empty;
            GoogleModel = "gemini-3.1-flash-lite-preview";
            DeepSeekApiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? string.Empty;
            DeepSeekModel = "deepseek-chat";
            DeepgramApiKey = Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY") ?? string.Empty;
            DeepgramLanguage = "en_fa";
            GroqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? string.Empty;
            GroqModel = "whisper-large-v3-turbo";
            SttProvider = "auto";
            VoiceMode = "cleanup";
            VoiceAiApiKey = Environment.GetEnvironmentVariable("VOICE_AI_API_KEY") ?? string.Empty;
            TtsProvider = "auto";
            AiProvider = "auto";
            FloatingEnabled = true;
            ShowOnTypingAndFocus = true;
            ShowMicOnShortWindow = true;
            ShowVoiceModeOnShortWindow = true;
            ShowReadAloudOnShortWindow = true;
        }

        private static string NormalizeModelName(string mdl)
        {
            if (string.IsNullOrEmpty(mdl)) return "gemini-3.1-flash-lite-preview";
            string m = mdl.Trim();
            if (m == "gemini-2.5-flash" || m == "gemini-2.5-flash-lite" || m == "gemini-2.5-pro" || m == "gemini-2.0-flash")
            {
                return "gemini-3.1-flash-lite-preview";
            }
            return m;
        }

        public static string NormalizeVoiceLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return "en_fa";
            string s = lang.Trim().ToLowerInvariant();
            if (s == "fa" || s == "persian" || s == "farsi") return "fa";
            if (s == "en" || s == "en-us" || s == "en-gb" || s == "english") return "en";
            return "en_fa";
        }

        private static string GetPrimarySettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "FERDOWSI");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "settings.json");
        }

        private static string GetLegacySettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "GrammerV", "settings.json");
        }

        public static GrammerVSettings Load()
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            string primaryPath = GetPrimarySettingsPath();
            string[] paths = new string[] { primaryPath, GetLegacySettingsPath() };
            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    if (File.Exists(paths[i]))
                    {
                        string raw = File.ReadAllText(paths[i], Encoding.UTF8);
                        GrammerVSettings loaded = js.Deserialize<GrammerVSettings>(raw);
                        if (loaded != null)
                        {
                            loaded.GoogleModel = NormalizeModelName(loaded.GoogleModel);
                            if (string.IsNullOrEmpty(loaded.DeepSeekModel)) loaded.DeepSeekModel = "deepseek-chat";
                            loaded.DeepgramLanguage = NormalizeVoiceLanguage(loaded.DeepgramLanguage);
                            if (string.IsNullOrEmpty(loaded.GroqModel)) loaded.GroqModel = "whisper-large-v3-turbo";
                            if (string.IsNullOrEmpty(loaded.SttProvider)) loaded.SttProvider = "auto";
                            if (string.IsNullOrEmpty(loaded.VoiceMode)) loaded.VoiceMode = "cleanup";
                            if (string.IsNullOrEmpty(loaded.TtsProvider)) loaded.TtsProvider = "auto";
                            if (string.IsNullOrEmpty(loaded.AiProvider)) loaded.AiProvider = "auto";
                            if (string.IsNullOrEmpty(loaded.GoogleApiKey))
                            {
                                loaded.GoogleApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                                                      ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                                                      ?? string.Empty;
                            }
                            if (string.IsNullOrEmpty(loaded.DeepSeekApiKey))
                            {
                                loaded.DeepSeekApiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? string.Empty;
                            }
                            if (string.IsNullOrEmpty(loaded.DeepgramApiKey))
                            {
                                loaded.DeepgramApiKey = Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY") ?? string.Empty;
                            }
                            if (string.IsNullOrEmpty(loaded.GroqApiKey))
                            {
                                loaded.GroqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? string.Empty;
                            }
                            if (string.IsNullOrEmpty(loaded.VoiceAiApiKey))
                            {
                                loaded.VoiceAiApiKey = Environment.GetEnvironmentVariable("VOICE_AI_API_KEY") ?? string.Empty;
                            }
                            if (i > 0 && !File.Exists(primaryPath))
                            {
                                loaded.Save();
                            }
                            return loaded;
                        }
                    }
                }
                catch { }
            }
            return new GrammerVSettings();
        }

        public void Save()
        {
            // Store personal settings & API keys ONLY in user's private %APPDATA%\FERDOWSI directory (never in the app/repo folder)
            JavaScriptSerializer js = new JavaScriptSerializer();
            string raw = js.Serialize(this);
            try { File.WriteAllText(GetPrimarySettingsPath(), raw, Encoding.UTF8); } catch { }
        }
    }

    public class GoogleTestResult
    {
        public bool Ok;
        public string Model;
        public string SampleOutput;
        public string Error;
        public double ElapsedMs;
    }

    public class VoiceTranscribeResult
    {
        public bool Ok;
        public string Transcript;
        public string RawTranscript;
        public string VoiceMode;
        public string Engine;
        public string Error;
        public double ElapsedMs;
    }

    public class ReadAloudResult
    {
        public bool Ok;
        public string WavPath;
        public string Engine;
        public string Error;
        public double ElapsedMs;
    }

    public class IssueItem
    {
        public string Original;
        public string Replacement;
        public string Category;
        public string Explanation;
    }

    public class DiffSegment
    {
        public string Op; // "eq", "del", "ins"
        public string Text;
    }

    public class AnalyzeResponse
    {
        public string Original;
        public string Corrected;
        public int IssueCount;
        public List<IssueItem> Issues;
        public List<DiffSegment> Diff;
        public string QuickSummary;
        public string Engine;
        public double ElapsedMs;

        public AnalyzeResponse()
        {
            Original = string.Empty;
            Corrected = string.Empty;
            Issues = new List<IssueItem>();
            Diff = new List<DiffSegment>();
            QuickSummary = "Ready";
            Engine = "C#+C++";
        }
    }

    public class TransformResponse
    {
        public string Mode;
        public string Result;
        public List<string> Variants;
        public string Engine;
        public double ElapsedMs;

        public TransformResponse()
        {
            Mode = string.Empty;
            Result = string.Empty;
            Variants = new List<string>();
            Engine = "C#+C++";
        }
    }

    public class DictionaryExampleItem
    {
        public string En;
        public string Fa;

        public DictionaryExampleItem()
        {
            En = string.Empty;
            Fa = string.Empty;
        }
    }

    public class DictionaryResponse
    {
        public bool Ok;
        public string Query;
        public bool IsSentence;
        public string Lang;
        public string Headword;
        public string PartOfSpeech;
        public string Phonetic;
        public string Definition;
        public string PersianTranslation;
        public string EnglishTranslation;
        public List<string> PersianMeanings;
        public List<string> Synonyms;
        public List<string> Antonyms;
        public List<DictionaryExampleItem> Examples;
        public string Engine;
        public double ElapsedMs;

        public DictionaryResponse()
        {
            Query = string.Empty;
            Lang = "en";
            Headword = string.Empty;
            PartOfSpeech = string.Empty;
            Phonetic = string.Empty;
            Definition = string.Empty;
            PersianTranslation = string.Empty;
            EnglishTranslation = string.Empty;
            PersianMeanings = new List<string>();
            Synonyms = new List<string>();
            Antonyms = new List<string>();
            Examples = new List<DictionaryExampleItem>();
            Engine = "AI Dictionary";
        }
    }

    public class PythonEngineClient : IDisposable
    {
        private readonly object _ioLock = new object();
        private readonly JavaScriptSerializer _json;
        private readonly string _scriptPath;
        private readonly string _dllPath;
        private Process _proc;
        private int _reqId;

        public GrammerVSettings Settings { get; private set; }
        public bool IsPythonConnected { get; private set; }
        public string PythonExePath { get; private set; }
        public string PythonVersion { get; private set; }
        public string LocalLlmStatus { get; private set; }
        public int PhiSilicaState { get; private set; }
        public string PhiSilicaLabel { get; private set; }
        public bool UseLocalAi
        {
            get { return Settings.AiProvider != "offline"; }
            set { Settings.AiProvider = value ? "auto" : "offline"; }
        }

        public bool HasGoogleKey
        {
            get { return !string.IsNullOrEmpty(Settings.GoogleApiKey) && Settings.GoogleApiKey.Trim().Length > 5; }
        }

        public bool HasDeepSeekKey
        {
            get { return !string.IsNullOrEmpty(Settings.DeepSeekApiKey) && Settings.DeepSeekApiKey.Trim().Length > 5; }
        }

        public bool HasDeepgramKey
        {
            get { return !string.IsNullOrEmpty(Settings.DeepgramApiKey) && Settings.DeepgramApiKey.Trim().Length > 5; }
        }

        public bool HasGroqKey
        {
            get { return !string.IsNullOrEmpty(Settings.GroqApiKey) && Settings.GroqApiKey.Trim().Length > 5; }
        }

        public bool HasAnySttKey
        {
            get { return HasDeepgramKey || HasGroqKey || HasGoogleKey; }
        }

        public bool HasVoiceAiKey
        {
            get { return !string.IsNullOrEmpty(Settings.VoiceAiApiKey) && Settings.VoiceAiApiKey.Trim().Length > 5; }
        }

        public bool HasAnyCloudAiKey
        {
            get { return HasGoogleKey || HasDeepSeekKey; }
        }

        public bool IsStrictAiPreferred
        {
            get
            {
                string p = (Settings.AiProvider ?? "auto").ToLowerInvariant();
                return p == "ai_only" || p == "google" || p == "deepseek";
            }
        }

        public event Action ConfigurationChanged;

        public PythonEngineClient(string scriptPath, string dllPath)
        {
            _scriptPath = scriptPath;
            _dllPath = dllPath;
            _json = new JavaScriptSerializer();
            _json.MaxJsonLength = 4 * 1024 * 1024;
            Settings = GrammerVSettings.Load();
            PythonVersion = "Not found (Fallback active)";
            LocalLlmStatus = "Auto-Detect";
            PhiSilicaState = -1;
            PhiSilicaLabel = "Probing...";
        }

        public bool Start()
        {
            if (NativeBridge.IsNativeLoaded)
            {
                try
                {
                    PhiSilicaState = NativeBridge.gv_phi_silica_status();
                    PhiSilicaLabel = DescribePhiState(PhiSilicaState);
                }
                catch { }
            }

            if (!File.Exists(_scriptPath))
            {
                IsPythonConnected = false;
                return false;
            }

            string pyExe = DiscoverPythonExecutable();
            if (string.IsNullOrEmpty(pyExe))
            {
                IsPythonConnected = false;
                return false;
            }

            PythonExePath = pyExe;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = pyExe;
                psi.Arguments = "-u \"" + _scriptPath + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                if (!string.IsNullOrEmpty(_dllPath) && File.Exists(_dllPath))
                {
                    psi.EnvironmentVariables["GRAMMERV_DLL_PATH"] = _dllPath;
                }
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";

                _proc = new Process();
                _proc.StartInfo = psi;
                _proc.Start();

                Dictionary<string, object> pingReq = new Dictionary<string, object>();
                pingReq["action"] = "ping";
                Dictionary<string, object> resp = SendRaw(pingReq);
                if (resp != null && GetBool(resp, "ok"))
                {
                    IsPythonConnected = true;
                    PythonVersion = GetString(resp, "python_version", "3.x");
                    if (resp.ContainsKey("phi_silica_state"))
                    {
                        PhiSilicaState = GetInt(resp, "phi_silica_state", PhiSilicaState);
                        PhiSilicaLabel = GetString(resp, "phi_silica_label", DescribePhiState(PhiSilicaState));
                    }
                    if (resp.ContainsKey("local_llm") && resp["local_llm"] is IDictionary)
                    {
                        IDictionary llm = (IDictionary)resp["local_llm"];
                        bool avail = llm.Contains("available") && Convert.ToBoolean(llm["available"]);
                        string prov = llm.Contains("provider") ? Convert.ToString(llm["provider"]) : "None";
                        LocalLlmStatus = avail ? ("Connected: " + prov) : "Standby (Phi Silica / Hybrid)";
                    }

                    PushConfigToPython();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Python engine start failed: " + ex.Message);
            }

            IsPythonConnected = false;
            return false;
        }

        public void ApplyAiConfiguration(string googleApiKey, string googleModel, string aiProvider, bool saveToDisk)
        {
            ApplyFullAiConfiguration(
                googleApiKey,
                googleModel,
                Settings.DeepSeekApiKey,
                Settings.DeepSeekModel,
                Settings.DeepgramApiKey,
                Settings.DeepgramLanguage,
                aiProvider,
                saveToDisk);
        }

        public void ApplyFullAiConfiguration()
        {
            PushConfigToPython();
            if (ConfigurationChanged != null)
            {
                ConfigurationChanged();
            }
        }

        public void ApplyFullAiConfiguration(
            string googleApiKey,
            string googleModel,
            string deepSeekApiKey,
            string deepSeekModel,
            string deepgramApiKey,
            string deepgramLanguage,
            string aiProvider,
            bool saveToDisk)
        {
            Settings.GoogleApiKey = (googleApiKey ?? string.Empty).Trim();
            Settings.GoogleModel = string.IsNullOrEmpty(googleModel) ? "gemini-3.1-flash-lite-preview" : googleModel.Trim();
            Settings.DeepSeekApiKey = (deepSeekApiKey ?? string.Empty).Trim();
            Settings.DeepSeekModel = string.IsNullOrEmpty(deepSeekModel) ? "deepseek-chat" : deepSeekModel.Trim();
            Settings.DeepgramApiKey = (deepgramApiKey ?? string.Empty).Trim();
            Settings.DeepgramLanguage = string.IsNullOrEmpty(deepgramLanguage) ? "multi" : deepgramLanguage.Trim().ToLowerInvariant();
            Settings.AiProvider = string.IsNullOrEmpty(aiProvider) ? "auto" : aiProvider.Trim().ToLowerInvariant();
            if (saveToDisk)
            {
                Settings.Save();
            }
            PushConfigToPython();
            if (ConfigurationChanged != null)
            {
                ConfigurationChanged();
            }
        }

        private void PushConfigToPython()
        {
            if (!IsPythonConnected) return;
            Dictionary<string, object> cfg = new Dictionary<string, object>();
            cfg["action"] = "configure";
            cfg["google_api_key"] = Settings.GoogleApiKey ?? string.Empty;
            cfg["google_model"] = Settings.GoogleModel ?? "gemini-3.1-flash-lite-preview";
            cfg["deepseek_api_key"] = Settings.DeepSeekApiKey ?? string.Empty;
            cfg["deepseek_model"] = Settings.DeepSeekModel ?? "deepseek-chat";
            cfg["deepgram_api_key"] = Settings.DeepgramApiKey ?? string.Empty;
            cfg["deepgram_language"] = Settings.DeepgramLanguage ?? "multi";
            cfg["groq_api_key"] = Settings.GroqApiKey ?? string.Empty;
            cfg["groq_model"] = Settings.GroqModel ?? "whisper-large-v3-turbo";
            cfg["stt_provider"] = Settings.SttProvider ?? "auto";
            cfg["voice_mode"] = Settings.VoiceMode ?? "cleanup";
            cfg["voice_ai_api_key"] = Settings.VoiceAiApiKey ?? string.Empty;
            cfg["tts_provider"] = Settings.TtsProvider ?? "auto";
            cfg["ai_provider"] = Settings.AiProvider ?? "auto";
            SendRaw(cfg);
        }

        public Task<GoogleTestResult> TestGoogleApiAsync(string apiKey, string model)
        {
            return Task.Run(delegate
            {
                GoogleTestResult res = new GoogleTestResult();
                res.Model = model;
                if (!IsPythonConnected)
                {
                    res.Ok = false;
                    res.Error = "Python NLP engine is not running.";
                    return res;
                }
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "test_google";
                req["google_api_key"] = (apiKey ?? string.Empty).Trim();
                req["google_model"] = string.IsNullOrEmpty(model) ? "gemini-3.1-flash-lite-preview" : model.Trim();
                Dictionary<string, object> resp = SendRaw(req);
                if (resp == null)
                {
                    res.Ok = false;
                    res.Error = "No response from Python engine.";
                    return res;
                }
                res.Ok = GetBool(resp, "ok");
                res.ElapsedMs = GetDouble(resp, "elapsed_ms", 0);
                if (res.Ok)
                {
                    res.Model = GetString(resp, "model", res.Model);
                    res.SampleOutput = GetString(resp, "sample_output", "");
                }
                else
                {
                    res.Error = GetString(resp, "error", "API test failed");
                }
                return res;
            });
        }

        public Task<GoogleTestResult> TestDeepSeekApiAsync(string apiKey, string model)
        {
            return Task.Run(delegate
            {
                GoogleTestResult res = new GoogleTestResult();
                res.Model = string.IsNullOrEmpty(model) ? "deepseek-chat" : model.Trim();
                if (!IsPythonConnected)
                {
                    res.Ok = false;
                    res.Error = "Python NLP engine is not running.";
                    return res;
                }
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "test_deepseek";
                req["deepseek_api_key"] = (apiKey ?? string.Empty).Trim();
                req["deepseek_model"] = res.Model;
                Dictionary<string, object> resp = SendRaw(req);
                if (resp == null)
                {
                    res.Ok = false;
                    res.Error = "No response from Python engine.";
                    return res;
                }
                res.Ok = GetBool(resp, "ok");
                res.ElapsedMs = GetDouble(resp, "elapsed_ms", 0);
                if (res.Ok)
                {
                    res.Model = GetString(resp, "model", res.Model);
                    res.SampleOutput = GetString(resp, "sample_output", "");
                }
                else
                {
                    res.Error = GetString(resp, "error", "DeepSeek API test failed");
                }
                return res;
            });
        }

        public Task<GoogleTestResult> TestDeepgramApiAsync(string apiKey)
        {
            return Task.Run(delegate
            {
                GoogleTestResult res = new GoogleTestResult();
                res.Model = "nova-2";
                if (!IsPythonConnected)
                {
                    res.Ok = false;
                    res.Error = "Python NLP engine is not running.";
                    return res;
                }
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "test_deepgram";
                req["deepgram_api_key"] = (apiKey ?? string.Empty).Trim();
                Dictionary<string, object> resp = SendRaw(req);
                if (resp == null)
                {
                    res.Ok = false;
                    res.Error = "No response from Python engine.";
                    return res;
                }
                res.Ok = GetBool(resp, "ok");
                res.ElapsedMs = GetDouble(resp, "elapsed_ms", 0);
                if (res.Ok)
                {
                    res.SampleOutput = GetString(resp, "sample_output", "Deepgram Voice ready!");
                }
                else
                {
                    res.Error = GetString(resp, "error", "Deepgram API test failed");
                }
                return res;
            });
        }

        public Task<GoogleTestResult> TestGroqApiAsync(string apiKey)
        {
            return Task.Run(delegate
            {
                GoogleTestResult res = new GoogleTestResult();
                res.Model = "whisper-large-v3-turbo";
                if (!IsPythonConnected)
                {
                    res.Ok = false;
                    res.Error = "Python NLP engine is not running.";
                    return res;
                }
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "test_groq";
                req["groq_api_key"] = (apiKey ?? string.Empty).Trim();
                Dictionary<string, object> resp = SendRaw(req);
                if (resp == null)
                {
                    res.Ok = false;
                    res.Error = "No response from Python engine.";
                    return res;
                }
                res.Ok = GetBool(resp, "ok");
                res.ElapsedMs = GetDouble(resp, "elapsed_ms", 0);
                if (res.Ok)
                {
                    res.SampleOutput = GetString(resp, "sample_output", "Groq Whisper Dictation ready!");
                }
                else
                {
                    res.Error = GetString(resp, "error", "Groq API test failed");
                }
                return res;
            });
        }

        public Task<VoiceTranscribeResult> TranscribeAudioAsync(string wavPath)
        {
            return Task.Run(delegate
            {
                VoiceTranscribeResult vr = new VoiceTranscribeResult();
                vr.Transcript = string.Empty;
                vr.Engine = "Voice Dictation";
                if (!IsPythonConnected)
                {
                    vr.Ok = false;
                    vr.Error = "Python engine is not running.";
                    return vr;
                }
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "transcribe_audio";
                req["wav_path"] = wavPath ?? string.Empty;
                req["deepgram_api_key"] = Settings.DeepgramApiKey ?? string.Empty;
                req["deepgram_language"] = Settings.DeepgramLanguage ?? "multi";
                req["groq_api_key"] = Settings.GroqApiKey ?? string.Empty;
                req["stt_provider"] = Settings.SttProvider ?? "auto";
                req["voice_mode"] = Settings.VoiceMode ?? "cleanup";
                Dictionary<string, object> resp = SendRaw(req);
                if (resp == null)
                {
                    vr.Ok = false;
                    vr.Error = "No response from voice transcription engine.";
                    return vr;
                }
                vr.Ok = GetBool(resp, "ok");
                vr.Transcript = GetString(resp, "transcript", string.Empty);
                vr.RawTranscript = GetString(resp, "raw_transcript", vr.Transcript);
                vr.VoiceMode = GetString(resp, "voice_mode", Settings.VoiceMode ?? "cleanup");
                vr.Engine = GetString(resp, "engine", "Voice Dictation");
                vr.Error = GetString(resp, "error", string.Empty);
                vr.ElapsedMs = GetDouble(resp, "elapsed_ms", 0);
                return vr;
            });
        }

        public Task<ReadAloudResult> ReadAloudAsync(string text, string outWavPath)
        {
            return Task.Run(delegate
            {
                ReadAloudResult rr = new ReadAloudResult();
                rr.WavPath = outWavPath ?? string.Empty;
                rr.Engine = "Read Aloud";
                if (!IsPythonConnected)
                {
                    rr.Ok = false;
                    rr.Error = "Python engine is not running.";
                    return rr;
                }
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "read_aloud";
                req["text"] = text ?? string.Empty;
                req["out_wav_path"] = outWavPath ?? string.Empty;
                req["tts_provider"] = Settings.TtsProvider ?? "auto";
                req["google_api_key"] = Settings.GoogleApiKey ?? string.Empty;
                req["deepgram_api_key"] = Settings.DeepgramApiKey ?? string.Empty;
                req["voice_ai_api_key"] = Settings.VoiceAiApiKey ?? string.Empty;
                Dictionary<string, object> resp = SendRaw(req);
                if (resp == null)
                {
                    rr.Ok = false;
                    rr.Error = "No response from Read Aloud engine.";
                    return rr;
                }
                rr.Ok = GetBool(resp, "ok");
                rr.WavPath = GetString(resp, "wav_path", outWavPath);
                rr.Engine = GetString(resp, "engine", "Read Aloud");
                rr.Error = GetString(resp, "error", string.Empty);
                rr.ElapsedMs = GetDouble(resp, "elapsed_ms", 0);
                return rr;
            });
        }

        public static string DescribePhiState(int code)
        {
            switch (code)
            {
                case 0: return "Ready (Copilot+ NPU Active)";
                case 1: return "Preparing NPU Model...";
                case 2: return "Not Supported on System";
                case 3: return "Disabled by Policy";
                case 4: return "Installed (Copilot+ NPU · Needs MSIX Identity)";
                case 5: return "NPU Hardware Required";
                case 6: return "Windows Update Needed";
                default: return "Unavailable";
            }
        }

        private string DiscoverPythonExecutable()
        {
            List<string> candidates = new List<string>();
            string envPy = Environment.GetEnvironmentVariable("GRAMMERV_PYTHON");
            if (!string.IsNullOrEmpty(envPy)) candidates.Add(envPy);

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string hermesRoot = Path.Combine(localApp, "hermes", "tools");
            if (Directory.Exists(hermesRoot))
            {
                try
                {
                    string[] dirs = Directory.GetDirectories(hermesRoot, "python*");
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        candidates.Add(Path.Combine(dirs[i], "python.exe"));
                    }
                }
                catch { }
            }

            string progPy = Path.Combine(localApp, "Programs", "Python");
            if (Directory.Exists(progPy))
            {
                try
                {
                    string[] dirs = Directory.GetDirectories(progPy, "Python3*");
                    for (int i = dirs.Length - 1; i >= 0; i--)
                    {
                        candidates.Add(Path.Combine(dirs[i], "python.exe"));
                    }
                }
                catch { }
            }

            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(userHome, "miniconda3", "python.exe"));
            candidates.Add(Path.Combine(userHome, "anaconda3", "python.exe"));

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] pathParts = pathEnv.Split(';');
            for (int i = 0; i < pathParts.Length; i++)
            {
                string dir = pathParts[i].Trim();
                if (dir.Length == 0) continue;
                if (dir.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string p = Path.Combine(dir, "python.exe");
                if (File.Exists(p)) candidates.Add(p);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string c = candidates[i];
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                {
                    return c;
                }
            }
            return null;
        }

        private Dictionary<string, object> SendRaw(Dictionary<string, object> req)
        {
            lock (_ioLock)
            {
                if (_proc == null || _proc.HasExited) return null;
                try
                {
                    int id = Interlocked.Increment(ref _reqId);
                    req["id"] = id;
                    string payload = _json.Serialize(req);
                    byte[] utf8 = Encoding.UTF8.GetBytes(payload + "\n");
                    _proc.StandardInput.BaseStream.Write(utf8, 0, utf8.Length);
                    _proc.StandardInput.BaseStream.Flush();

                    string line = _proc.StandardOutput.ReadLine();
                    if (string.IsNullOrEmpty(line)) return null;
                    return _json.Deserialize<Dictionary<string, object>>(line);
                }
                catch
                {
                    return null;
                }
            }
        }

        public Task<AnalyzeResponse> AnalyzeAsync(string text)
        {
            return AnalyzeAsync(text, false);
        }

        public Task<AnalyzeResponse> AnalyzeAsync(string text, bool fastBadgeOnly)
        {
            return Task.Run(delegate { return Analyze(text, fastBadgeOnly); });
        }

        public Task<TransformResponse> RewordAsync(string text, string mode, int variant)
        {
            return Task.Run(delegate { return Reword(text, mode, variant); });
        }

        public Task<TransformResponse> FormatAsync(string text, string mode)
        {
            return Task.Run(delegate { return Format(text, mode); });
        }

        public AnalyzeResponse Analyze(string text)
        {
            return Analyze(text, false);
        }

        public AnalyzeResponse Analyze(string text, bool fastBadgeOnly)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                return new AnalyzeResponse();
            }

            if (IsPythonConnected)
            {
                bool isAiOnly = (Settings.AiProvider == "ai_only");
                bool effectiveFastBadge = isAiOnly ? false : fastBadgeOnly;
                bool shouldUseAi = !effectiveFastBadge && (Settings.AiProvider != "offline") &&
                                   (HasAnyCloudAiKey || PhiSilicaState == 0 || isAiOnly || Settings.AiProvider == "local");
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "analyze";
                req["text"] = text;
                req["use_llm"] = shouldUseAi;
                req["fast_badge_only"] = effectiveFastBadge;
                Dictionary<string, object> resp = SendRaw(req);
                if (resp != null && GetBool(resp, "ok"))
                {
                    sw.Stop();
                    AnalyzeResponse ar = new AnalyzeResponse();
                    ar.Original = text;
                    ar.Corrected = GetString(resp, "corrected", text);
                    ar.IssueCount = GetInt(resp, "issue_count", 0);
                    ar.QuickSummary = GetString(resp, "quick_summary", "Looks clean ✓");
                    ar.Engine = GetString(resp, "engine", "Python+C++");
                    ar.ElapsedMs = GetDouble(resp, "elapsed_ms", sw.Elapsed.TotalMilliseconds);

                    if (resp.ContainsKey("issues") && resp["issues"] is IEnumerable)
                    {
                        foreach (object item in (IEnumerable)resp["issues"])
                        {
                            IDictionary d = item as IDictionary;
                            if (d != null)
                            {
                                IssueItem iss = new IssueItem();
                                iss.Original = d.Contains("original") ? Convert.ToString(d["original"]) : "";
                                iss.Replacement = d.Contains("replacement") ? Convert.ToString(d["replacement"]) : "";
                                iss.Category = d.Contains("category") ? Convert.ToString(d["category"]) : "Grammar";
                                iss.Explanation = d.Contains("explanation") ? Convert.ToString(d["explanation"]) : "";
                                ar.Issues.Add(iss);
                            }
                        }
                    }

                    if (resp.ContainsKey("diff") && resp["diff"] is IEnumerable)
                    {
                        foreach (object item in (IEnumerable)resp["diff"])
                        {
                            IDictionary d = item as IDictionary;
                            if (d != null)
                            {
                                DiffSegment seg = new DiffSegment();
                                seg.Op = d.Contains("op") ? Convert.ToString(d["op"]) : "eq";
                                seg.Text = d.Contains("text") ? Convert.ToString(d["text"]) : "";
                                ar.Diff.Add(seg);
                            }
                        }
                    }
                    return ar;
                }
            }

            return FallbackAnalyze(text, sw);
        }

        public TransformResponse Reword(string text, string mode, int variant)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(text)) return new TransformResponse();

            if (IsPythonConnected)
            {
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "reword";
                req["text"] = text;
                req["mode"] = mode;
                req["variant"] = variant;
                req["use_llm"] = (Settings.AiProvider != "offline");
                Dictionary<string, object> resp = SendRaw(req);
                if (resp != null && GetBool(resp, "ok"))
                {
                    sw.Stop();
                    TransformResponse tr = new TransformResponse();
                    tr.Mode = mode;
                    tr.Result = GetString(resp, "result", text);
                    tr.Engine = GetString(resp, "engine", "Python+C++");
                    tr.ElapsedMs = GetDouble(resp, "elapsed_ms", sw.Elapsed.TotalMilliseconds);
                    if (resp.ContainsKey("variants") && resp["variants"] is IEnumerable)
                    {
                        foreach (object v in (IEnumerable)resp["variants"])
                        {
                            tr.Variants.Add(Convert.ToString(v));
                        }
                    }
                    if (tr.Variants.Count == 0) tr.Variants.Add(tr.Result);
                    return tr;
                }
            }

            TransformResponse fb = new TransformResponse();
            fb.Mode = mode;
            AnalyzeResponse clean = FallbackAnalyze(text, sw);
            fb.Result = clean.Corrected;
            fb.Variants.Add(fb.Result);
            sw.Stop();
            fb.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return fb;
        }

        public TransformResponse Format(string text, string mode)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(text)) return new TransformResponse();

            if (IsPythonConnected)
            {
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "format";
                req["text"] = text;
                req["mode"] = mode;
                req["use_llm"] = (Settings.AiProvider != "offline") && (HasAnyCloudAiKey || Settings.AiProvider == "ai_only" || PhiSilicaState == 0);
                Dictionary<string, object> resp = SendRaw(req);
                if (resp != null && GetBool(resp, "ok"))
                {
                    sw.Stop();
                    TransformResponse tr = new TransformResponse();
                    tr.Mode = mode;
                    tr.Result = GetString(resp, "result", text);
                    string defaultFmtEng = (HasAnyCloudAiKey && Settings.AiProvider != "offline")
                        ? (HasDeepSeekKey && Settings.AiProvider == "deepseek"
                            ? ("DeepSeek (" + Settings.DeepSeekModel + ")")
                            : ("Google Gemini (" + Settings.GoogleModel + ")"))
                        : "Python+C++";
                    tr.Engine = GetString(resp, "engine", defaultFmtEng);
                    tr.ElapsedMs = GetDouble(resp, "elapsed_ms", sw.Elapsed.TotalMilliseconds);
                    tr.Variants.Add(tr.Result);
                    return tr;
                }
            }

            TransformResponse fb = new TransformResponse();
            fb.Mode = mode;
            if (mode == "upper_case") fb.Result = text.ToUpperInvariant();
            else if (mode == "lower_case") fb.Result = text.ToLowerInvariant();
            else fb.Result = text.Trim();
            fb.Variants.Add(fb.Result);
            sw.Stop();
            fb.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return fb;
        }

        public Task<DictionaryResponse> DictionaryLookupAsync(string text)
        {
            return Task.Run(delegate { return DictionaryLookup(text, true); });
        }

        public DictionaryResponse DictionaryLookup(string text)
        {
            return DictionaryLookup(text, true);
        }

        public DictionaryResponse DictionaryLookup(string text, bool useAi)
        {
            Stopwatch sw = Stopwatch.StartNew();
            DictionaryResponse dr = new DictionaryResponse();
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                return dr;
            }

            dr.Query = text.Trim();
            if (IsPythonConnected)
            {
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["action"] = "dictionary";
                req["text"] = text;
                req["use_llm"] = useAi;
                Dictionary<string, object> resp = SendRaw(req);
                if (resp != null && GetBool(resp, "ok"))
                {
                    sw.Stop();
                    dr.Ok = true;
                    dr.Query = GetString(resp, "query", dr.Query);
                    dr.IsSentence = GetBool(resp, "is_sentence");
                    dr.Lang = GetString(resp, "lang", "en");
                    dr.Headword = GetString(resp, "headword", dr.Query);
                    dr.PartOfSpeech = GetString(resp, "part_of_speech", "");
                    dr.Phonetic = GetString(resp, "phonetic", "");
                    dr.Definition = GetString(resp, "definition", "");
                    dr.PersianTranslation = GetString(resp, "persian_translation", "");
                    dr.EnglishTranslation = GetString(resp, "english_translation", "");
                    dr.Engine = GetString(resp, "engine", "Google Gemini AI");
                    dr.ElapsedMs = GetDouble(resp, "elapsed_ms", sw.Elapsed.TotalMilliseconds);

                    if (resp.ContainsKey("persian_meanings") && resp["persian_meanings"] is IEnumerable)
                    {
                        foreach (object item in (IEnumerable)resp["persian_meanings"])
                        {
                            string s = Convert.ToString(item);
                            if (!string.IsNullOrEmpty(s)) dr.PersianMeanings.Add(s);
                        }
                    }

                    if (resp.ContainsKey("synonyms") && resp["synonyms"] is IEnumerable)
                    {
                        foreach (object item in (IEnumerable)resp["synonyms"])
                        {
                            string s = Convert.ToString(item);
                            if (!string.IsNullOrEmpty(s)) dr.Synonyms.Add(s);
                        }
                    }

                    if (resp.ContainsKey("antonyms") && resp["antonyms"] is IEnumerable)
                    {
                        foreach (object item in (IEnumerable)resp["antonyms"])
                        {
                            string s = Convert.ToString(item);
                            if (!string.IsNullOrEmpty(s)) dr.Antonyms.Add(s);
                        }
                    }

                    if (resp.ContainsKey("examples") && resp["examples"] is IEnumerable)
                    {
                        foreach (object item in (IEnumerable)resp["examples"])
                        {
                            IDictionary d = item as IDictionary;
                            if (d != null)
                            {
                                DictionaryExampleItem ex = new DictionaryExampleItem();
                                ex.En = d.Contains("en") ? Convert.ToString(d["en"]) : "";
                                ex.Fa = d.Contains("fa") ? Convert.ToString(d["fa"]) : "";
                                if (!string.IsNullOrEmpty(ex.En) || !string.IsNullOrEmpty(ex.Fa))
                                {
                                    dr.Examples.Add(ex);
                                }
                            }
                        }
                    }
                    return dr;
                }
            }

            sw.Stop();
            dr.Headword = dr.Query;
            dr.Definition = "Python NLP / AI engine unavailable.";
            dr.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return dr;
        }

        private AnalyzeResponse FallbackAnalyze(string text, Stopwatch sw)
        {
            AnalyzeResponse ar = new AnalyzeResponse();
            ar.Original = text;
            string corrected = Regex.Replace(text, @"[A-Za-z]+", delegate(Match m)
            {
                string w = m.Value;
                if (w == "i")
                {
                    ar.Issues.Add(new IssueItem { Original = "i", Replacement = "I", Category = "Capitalization", Explanation = "Capitalize pronoun 'I'" });
                    return "I";
                }
                string sug = NativeBridge.NativeSpellSuggest(w);
                if (!string.IsNullOrEmpty(sug) && sug != w)
                {
                    ar.Issues.Add(new IssueItem { Original = w, Replacement = sug, Category = "Spelling", Explanation = "C++ Damerau-Levenshtein fix" });
                    return sug;
                }
                return w;
            });
            ar.Corrected = corrected;
            ar.IssueCount = ar.Issues.Count;
            ar.Diff.Add(new DiffSegment { Op = "eq", Text = corrected });
            ar.QuickSummary = ar.IssueCount > 0 ? (ar.IssueCount + " fixes") : "Looks clean ✓";
            sw.Stop();
            ar.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return ar;
        }

        private static bool GetBool(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key) && d[key] != null && Convert.ToBoolean(d[key]);
        }

        private static string GetString(Dictionary<string, object> d, string key, string defVal)
        {
            return (d != null && d.ContainsKey(key) && d[key] != null) ? Convert.ToString(d[key]) : defVal;
        }

        private static int GetInt(Dictionary<string, object> d, string key, int defVal)
        {
            return (d != null && d.ContainsKey(key) && d[key] != null) ? Convert.ToInt32(d[key]) : defVal;
        }

        private static double GetDouble(Dictionary<string, object> d, string key, double defVal)
        {
            return (d != null && d.ContainsKey(key) && d[key] != null) ? Convert.ToDouble(d[key]) : defVal;
        }

        public void Dispose()
        {
            lock (_ioLock)
            {
                if (_proc != null)
                {
                    try
                    {
                        if (!_proc.HasExited)
                        {
                            byte[] quit = Encoding.UTF8.GetBytes("{\"action\":\"shutdown\"}\n");
                            _proc.StandardInput.BaseStream.Write(quit, 0, quit.Length);
                            _proc.StandardInput.BaseStream.Flush();
                            if (!_proc.WaitForExit(350)) _proc.Kill();
                        }
                    }
                    catch
                    {
                        try { _proc.Kill(); } catch { }
                    }
                    _proc.Dispose();
                    _proc = null;
                }
            }
        }
    }
}
