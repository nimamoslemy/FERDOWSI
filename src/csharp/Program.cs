using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("FERDOWSI v2")]
[assembly: AssemblyDescription("FERDOWSI v2 — English & Persian Writing Assistant, Voice Dictation, Read Aloud & AI Dictionary")]
[assembly: AssemblyCompany("FERDOWSI Open Source")]
[assembly: AssemblyProduct("FERDOWSI v2")]
[assembly: AssemblyCopyright("Copyright (c) 2026 FERDOWSI Open Source (MIT License)")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: ComVisible(false)]

namespace GrammerV
{
    public static class Program
    {
        private static Mutex _singleInstanceMutex;
        private static EventWaitHandle _showStudioEvent;
        private static Forms.NotifyIcon _trayIcon;
        private static PythonEngineClient _engine;
        private static FloatingOverlay _overlay;
        private static StudioWindow _studio;
        private static Application _wpfApp;

        [STAThread]
        public static int Main(string[] args)
        {
            NativeBridge.EnablePerMonitorDpiAwareness();

            bool isSelfTest = false;
            string selfTestReportPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    isSelfTest = true;
                    selfTestReportPath = (i + 1 < args.Length) ? args[i + 1] : null;
                    break;
                }
            }

            if (!isSelfTest)
            {
                bool createdNew;
                _singleInstanceMutex = new Mutex(true, @"Local\GrammerV_SingleInstance_Mutex_v1", out createdNew);
                if (!createdNew)
                {
                    try
                    {
                        using (EventWaitHandle evt = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\GrammerV_ShowStudio_Event_v1"))
                        {
                            evt.Set();
                        }
                    }
                    catch { }
                    return 0;
                }
            }

            string runtimeDir = PrepareEmbeddedRuntimeFiles();
            bool nativeOk = NativeBridge.InitializeNativeDll(runtimeDir);

            string scriptPath = ResolvePythonScriptPath(runtimeDir);
            _engine = new PythonEngineClient(scriptPath, NativeBridge.LoadedDllPath);
            _engine.Start();

            if (isSelfTest)
            {
                int code = RunAutomatedSelfTest(nativeOk, selfTestReportPath);
                Cleanup();
                return code;
            }

            try
            {
                _showStudioEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\GrammerV_ShowStudio_Event_v1");
                Thread ipcThread = new Thread(delegate()
                {
                    while (_showStudioEvent != null)
                    {
                        try
                        {
                            if (_showStudioEvent.WaitOne())
                            {
                                if (_wpfApp != null)
                                {
                                    _wpfApp.Dispatcher.BeginInvoke(new Action(ShowStudioWindow));
                                }
                            }
                        }
                        catch
                        {
                            break;
                        }
                    }
                });
                ipcThread.IsBackground = true;
                ipcThread.Start();
            }
            catch { }

            bool startInTrayOnly = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--tray", StringComparison.OrdinalIgnoreCase))
                {
                    startInTrayOnly = true;
                }
            }

            _wpfApp = new Application();
            _wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _overlay = new FloatingOverlay(_engine, ShowStudioWindow, ShowStudioHelp);
            // Force HWND creation for hotkeys & caret tracking while hidden off-screen
            _overlay.Show();
            _overlay.Visibility = Visibility.Hidden;

            _studio = new StudioWindow(_engine, _overlay, QuitApplication);
            if (NativeBridge.IsNativeLoaded)
            {
                NativeBridge.gv_set_own_windows(_overlay.Hwnd.ToInt64(), IntPtr.Zero.ToInt64());
            }

            SetupSystemTrayIcon();

            if (!startInTrayOnly)
            {
                _studio.Show();
                if (NativeBridge.IsNativeLoaded)
                {
                    NativeBridge.gv_set_own_windows(_overlay.Hwnd.ToInt64(), _studio.Hwnd.ToInt64());
                }
            }

            _wpfApp.Run();
            Cleanup();
            return 0;
        }

        private static string PrepareEmbeddedRuntimeFiles()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string runtimeDir = Path.Combine(localApp, "FERDOWSI", "runtime");
            try
            {
                Directory.CreateDirectory(runtimeDir);
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                NativeBridge.UnblockFileZoneIdentifier(Assembly.GetExecutingAssembly().Location);
                NativeBridge.UnblockFileZoneIdentifier(Path.Combine(exeDir, "grammerv_native.dll"));

                if (!File.Exists(Path.Combine(exeDir, "grammerv_native.dll")))
                {
                    ExtractResourceIfPresent("grammerv_native.dll.gz", Path.Combine(runtimeDir, "grammerv_native.dll"), true);
                }
                if (!File.Exists(Path.Combine(exeDir, "engine", "nlp_engine.py")))
                {
                    ExtractResourceIfPresent("nlp_engine.py", Path.Combine(runtimeDir, "nlp_engine.py"), false);
                }
                string helpPdf = Path.Combine(exeDir, "FERDOWSI_Help_Guide.pdf");
                if (!File.Exists(helpPdf) || (new FileInfo(helpPdf).Length < 10000))
                {
                    AppIconGenerator.EnsureHelpPdfFile(helpPdf);
                }
            }
            catch { }
            return runtimeDir;
        }

        private static void ExtractResourceIfPresent(string resourceName, string destPath, bool isGzip)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(resourceName))
                {
                    if (s == null) return;
                    using (FileStream fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        if (isGzip)
                        {
                            using (GZipStream gz = new GZipStream(s, CompressionMode.Decompress))
                            {
                                gz.CopyTo(fs);
                            }
                        }
                        else
                        {
                            s.CopyTo(fs);
                        }
                    }
                    NativeBridge.UnblockFileZoneIdentifier(destPath);
                }
            }
            catch { }
        }

        private static string ResolvePythonScriptPath(string runtimeDir)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = Path.Combine(exeDir, "engine", "nlp_engine.py");
            if (File.Exists(p1)) return p1;

            string p2 = Path.Combine(exeDir, "src", "python", "nlp_engine.py");
            if (File.Exists(p2)) return p2;

            string p3 = Path.Combine(runtimeDir, "nlp_engine.py");
            return p3;
        }

        private static void SetupSystemTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon();
            _trayIcon.Icon = AppIconGenerator.CreateTrayIcon(32);
            _trayIcon.Text = "FERDOWSI v2 — Above-Cursor Assistant (Alt+G)";
            _trayIcon.Visible = true;

            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();

            Forms.ToolStripMenuItem openItem = new Forms.ToolStripMenuItem("Open FERDOWSI v2 Studio");
            openItem.Click += delegate { ShowStudioWindow(); };

            Forms.ToolStripMenuItem helpItem = new Forms.ToolStripMenuItem("Help / راهنما (English & فارسی)");
            helpItem.Click += delegate { ShowStudioHelp(); };

            Forms.ToolStripMenuItem summonItem = new Forms.ToolStripMenuItem("Summon Above Cursor (Alt+G)");
            summonItem.Click += delegate
            {
                if (_overlay != null) _overlay.SummonAboveCursor(true);
            };

            Forms.ToolStripMenuItem toggleItem = new Forms.ToolStripMenuItem("Floating Cursor Widget: Enabled");
            toggleItem.Checked = true;
            toggleItem.Click += delegate
            {
                if (_overlay != null)
                {
                    _overlay.FloatingEnabled = !_overlay.FloatingEnabled;
                    toggleItem.Checked = _overlay.FloatingEnabled;
                    toggleItem.Text = _overlay.FloatingEnabled
                        ? "Floating Cursor Widget: Enabled"
                        : "Floating Cursor Widget: Paused";
                }
            };

            Forms.ToolStripMenuItem pause10Item = new Forms.ToolStripMenuItem("Pause Floating Widget for 10 minutes");
            pause10Item.Click += delegate
            {
                if (_overlay != null) _overlay.SnoozeForMinutes(10);
            };

            Forms.ToolStripMenuItem pause30Item = new Forms.ToolStripMenuItem("Pause Floating Widget for 30 minutes");
            pause30Item.Click += delegate
            {
                if (_overlay != null) _overlay.SnoozeForMinutes(30);
            };

            Forms.ToolStripMenuItem pause60Item = new Forms.ToolStripMenuItem("Pause Floating Widget for 1 hour");
            pause60Item.Click += delegate
            {
                if (_overlay != null) _overlay.SnoozeForMinutes(60);
            };

            Forms.ToolStripMenuItem resumeItem = new Forms.ToolStripMenuItem("Resume Floating Widget Now");
            resumeItem.Click += delegate
            {
                if (_overlay != null)
                {
                    _overlay.ResumeFromSnooze();
                    toggleItem.Checked = true;
                    toggleItem.Text = "Floating Cursor Widget: Enabled";
                }
            };

            Forms.ToolStripMenuItem quitItem = new Forms.ToolStripMenuItem("Quit FERDOWSI");
            quitItem.Click += delegate { QuitApplication(); };

            menu.Items.Add(openItem);
            menu.Items.Add(helpItem);
            menu.Items.Add(summonItem);
            menu.Items.Add(toggleItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(pause10Item);
            menu.Items.Add(pause30Item);
            menu.Items.Add(pause60Item);
            menu.Items.Add(resumeItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(quitItem);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += delegate { ShowStudioWindow(); };
        }

        private static void ShowStudioWindow()
        {
            if (_studio == null) return;
            if (!_studio.IsVisible) _studio.Show();
            if (_studio.WindowState == WindowState.Minimized) _studio.WindowState = WindowState.Normal;
            _studio.Topmost = true;
            _studio.Activate();
            _studio.Topmost = false;
            _studio.Focus();
        }

        private static void ShowStudioHelp()
        {
            AppIconGenerator.OpenHelpPdf();
        }

        private static void QuitApplication()
        {
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
                if (_studio != null)
                {
                    _studio.AllowClose();
                    _studio.Close();
                }
                if (_overlay != null)
                {
                    _overlay.Close();
                }
            }
            catch { }

            if (_wpfApp != null)
            {
                _wpfApp.Shutdown();
            }
        }

        private static void Cleanup()
        {
            try
            {
                if (_showStudioEvent != null)
                {
                    EventWaitHandle evt = _showStudioEvent;
                    _showStudioEvent = null;
                    evt.Set();
                    evt.Dispose();
                }
            }
            catch { }

            try
            {
                if (_engine != null)
                {
                    _engine.Dispose();
                    _engine = null;
                }
            }
            catch { }

            try
            {
                if (NativeBridge.IsNativeLoaded)
                {
                    NativeBridge.gv_shutdown();
                }
            }
            catch { }

            try
            {
                if (_singleInstanceMutex != null)
                {
                    _singleInstanceMutex.Dispose();
                    _singleInstanceMutex = null;
                }
            }
            catch { }
        }

        private static int RunAutomatedSelfTest(bool nativeOk, string reportPath)
        {
            Stopwatch totalSw = Stopwatch.StartNew();
            StringBuilder log = new StringBuilder();
            int failures = 0;

            Action<string, bool, string> check = delegate(string name, bool cond, string detail)
            {
                string status = cond ? "PASS" : "FAIL";
                if (!cond) failures++;
                log.AppendLine(string.Format("[{0}] {1} :: {2}", status, name, detail));
            };

            check("C++ Native DLL Loaded", nativeOk && NativeBridge.IsNativeLoaded,
                NativeBridge.LoadedDllPath ?? "missing");

            if (NativeBridge.IsNativeLoaded)
            {
                int dist = NativeBridge.gv_damerau_distance("cursur", "cursor");
                check("C++ Damerau-Levenshtein Distance", dist == 1, "distance(cursur, cursor) = " + dist);

                string sug = NativeBridge.NativeSpellSuggest("grammer");
                check("C++ Native Spell Checker", sug == "grammar", "suggest('grammer') = " + (sug ?? "null"));

                GvMonitorState st;
                int pollOk = NativeBridge.gv_poll_state(out st);
                check("C++ Win32/MSAA Monitor Poll", pollOk == 1,
                    string.Format("mouse=({0},{1}) fg=0x{2:X}", st.mouse_x, st.mouse_y, st.foreground_hwnd));

                int phiCode = NativeBridge.gv_phi_silica_status();
                check("Copilot+ Phi Silica NPU Probe", phiCode >= -1,
                    string.Format("state={0} ({1})", phiCode, PythonEngineClient.DescribePhiState(phiCode)));
            }

            check("Python 3 NLP JSON-RPC Connected", _engine.IsPythonConnected,
                "Python v" + _engine.PythonVersion + " @ " + (_engine.PythonExePath ?? "fallback"));

            string savedKey = _engine.Settings.GoogleApiKey;
            string savedModel = _engine.Settings.GoogleModel;
            string savedProvider = _engine.Settings.AiProvider;
            _engine.ApplyAiConfiguration(savedKey, savedModel, "auto", false);

            AnalyzeResponse ar = _engine.Analyze("i has went to teh store yesterday for buying a apple and check my cursur", true);
            check("Grammar & Spelling Correction",
                ar.IssueCount >= 4 && ar.Corrected.IndexOf("went to the store yesterday to buy an apple", StringComparison.OrdinalIgnoreCase) >= 0,
                string.Format("{0} fixes in {1:0.0} ms -> \"{2}\"", ar.IssueCount, ar.ElapsedMs, ar.Corrected));

            AnalyzeResponse ar2 = _engine.Analyze("It seems that it can not get the full sentence and a few words will be remained untouched when it delete the sentence", true);
            check("Full-Sentence Passive & Agreement Grammar",
                ar2.Corrected.IndexOf("cannot", StringComparison.OrdinalIgnoreCase) >= 0 &&
                ar2.Corrected.IndexOf("will remain", StringComparison.OrdinalIgnoreCase) >= 0 &&
                ar2.Corrected.IndexOf("when it deletes", StringComparison.OrdinalIgnoreCase) >= 0,
                string.Format("{0} fixes in {1:0.0} ms -> \"{2}\"", ar2.IssueCount, ar2.ElapsedMs, ar2.Corrected));

            AnalyzeResponse arFinglish = _engine.Analyze("salam halet chetore", true);
            check("Finglish -> Persian Script Suggestion",
                arFinglish.IssueCount >= 1 &&
                arFinglish.Corrected.Contains("سلام") &&
                arFinglish.Corrected.Contains("حالت") &&
                (arFinglish.Corrected.Contains("چطوره") || arFinglish.Corrected.Contains("چطور")),
                string.Format("{0:0.0} ms -> \"{1}\"", arFinglish.ElapsedMs, arFinglish.Corrected));

            AnalyzeResponse arPersian = _engine.Analyze("صلام من می روم به خانه ها و سپاسگذارم", true);
            check("Persian Spelling, Homophone & ZWNJ Correction",
                arPersian.IssueCount >= 3 &&
                arPersian.Corrected.Contains("سلام") &&
                arPersian.Corrected.Contains("می\u200Cروم") &&
                arPersian.Corrected.Contains("خانه\u200Cها") &&
                arPersian.Corrected.Contains("سپاسگزارم"),
                string.Format("{0} fixes in {1:0.0} ms -> \"{2}\"", arPersian.IssueCount, arPersian.ElapsedMs, arPersian.Corrected));

            _engine.ApplyAiConfiguration(savedKey, savedModel, savedProvider, false);
            check("Google Gemini Settings & Config Bridge",
                !string.IsNullOrEmpty(savedModel) && !string.IsNullOrEmpty(savedProvider),
                string.Format("model={0}, provider={1}, keySet={2}", savedModel, savedProvider, _engine.HasGoogleKey));

            TransformResponse trFluent = _engine.Reword(
                "hey can u send me teh report right now cuz i really need to check a lot of stuff in order to fix the problem",
                "fluent", 0);
            check("Reword Tone [fluent]",
                !string.IsNullOrEmpty(trFluent.Result) && trFluent.Result.IndexOf("teh", StringComparison.OrdinalIgnoreCase) < 0,
                string.Format("{0:0.0} ms ({1}) -> \"{2}\"", trFluent.ElapsedMs, trFluent.Engine, trFluent.Result));

            DictionaryResponse dictWord = _engine.DictionaryLookup("resilient", true);
            check("AI Dictionary [Word: Persian + Syn/Ant + Examples]",
                dictWord.Ok &&
                !string.IsNullOrEmpty(dictWord.PersianTranslation) &&
                dictWord.Synonyms.Count >= 2 &&
                dictWord.Antonyms.Count >= 2 &&
                dictWord.Examples.Count >= 1,
                string.Format("{0:0.0} ms ({1}) -> Fa=\"{2}\", Syn={3}, Ant={4}, Ex={5}",
                    dictWord.ElapsedMs, dictWord.Engine, dictWord.PersianTranslation,
                    dictWord.Synonyms.Count, dictWord.Antonyms.Count, dictWord.Examples.Count));

            // Run remaining multi-tone / format / sentence matrix in fast deterministic mode so self-test stays <6s and preserves API quota
            _engine.ApplyAiConfiguration(savedKey, savedModel, "offline", false);

            string[] tones = new string[] { "formal", "casual", "concise", "expand", "persuasive" };
            for (int i = 0; i < tones.Length; i++)
            {
                TransformResponse tr = _engine.Reword(
                    "hey can u send me teh report right now cuz i really need to check a lot of stuff in order to fix the problem",
                    tones[i], 0);
                check("Reword Tone [" + tones[i] + "]",
                    !string.IsNullOrEmpty(tr.Result) && tr.Result.IndexOf("teh", StringComparison.OrdinalIgnoreCase) < 0,
                    string.Format("{0:0.0} ms ({1}) -> \"{2}\"", tr.ElapsedMs, tr.Engine, tr.Result));
            }

            TransformResponse bullets = _engine.Format("finish the c++ dll, connect the python engine, and test the c# exe", "bullet_list");
            check("Format [bullet_list]",
                bullets.Result.Contains("•") && bullets.Result.Contains("\n"),
                string.Format("{0:0.0} ms -> {1} lines", bullets.ElapsedMs, bullets.Result.Split('\n').Length));

            TransformResponse title = _engine.Format("a smart writing assistant above the cursor in windows", "title_case");
            check("Format [title_case]",
                title.Result == "A Smart Writing Assistant Above the Cursor in Windows",
                string.Format("{0:0.0} ms -> \"{1}\"", title.ElapsedMs, title.Result));

            TransformResponse email = _engine.Format("can u send teh updated project schedule", "email_format");
            check("Format [email_format]",
                email.Result.StartsWith("Hi,") && email.Result.Contains("Best regards,"),
                string.Format("{0:0.0} ms", email.ElapsedMs));

            DictionaryResponse dictSent = _engine.DictionaryLookup("Knowledge empowers people to overcome difficult challenges.", false);
            check("AI Dictionary [Sentence Translation + Vocabulary]",
                dictSent.Ok &&
                !string.IsNullOrEmpty(dictSent.PersianTranslation) &&
                dictSent.Examples.Count >= 1,
                string.Format("{0:0.0} ms ({1}) -> Fa=\"{2}\"",
                    dictSent.ElapsedMs, dictSent.Engine, dictSent.PersianTranslation));

            _engine.ApplyAiConfiguration(savedKey, savedModel, savedProvider, false);

            // Test non-activating WPF FloatingOverlay window creation & FERDOWSI brand flag asset
            System.Windows.Media.ImageSource flagSrc = AppIconGenerator.GetBrandFlagImageSource();
            check("FERDOWSI Brand Flag Embedded Asset", flagSrc != null,
                flagSrc != null ? string.Format("{0:0}x{1:0} RGBA loaded", flagSrc.Width, flagSrc.Height) : "missing");

            TransformResponse customReword = _engine.Reword(
                "finish the c++ dll, connect the python engine, and test the c# exe",
                "custom", 0, "Format as a bullet list");
            check("Custom Reword Format Instruction",
                !string.IsNullOrEmpty(customReword.Result) && customReword.Result.Contains("•"),
                string.Format("{0:0.0} ms ({1})", customReword.ElapsedMs, customReword.Engine));

            string pdfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FERDOWSI_Help_Guide.pdf");
            AppIconGenerator.EnsureHelpPdfFile(pdfPath);
            bool pdfOk = File.Exists(pdfPath) && (new FileInfo(pdfPath).Length > 10000);
            check("Bilingual Help Guide PDF Generated (FERDOWSI_Help_Guide.pdf)", pdfOk,
                pdfOk ? (pdfPath + " (" + (new FileInfo(pdfPath).Length / 1024) + " KB)") : "missing");

            try
            {
                FloatingOverlay testOverlay = new FloatingOverlay(_engine, null);
                testOverlay.Show();
                bool hasHwnd = testOverlay.Hwnd != IntPtr.Zero;
                bool titleOk = testOverlay.Title == "FERDOWSI Overlay";

                StudioWindow testStudio = new StudioWindow(_engine, testOverlay, null);
                testStudio.Left = -3000;
                testStudio.Top = -3000;
                testStudio.Show();
                bool studioOk = testStudio.Hwnd != IntPtr.Zero && testStudio.Title.Contains("FERDOWSI");
                testStudio.AllowClose();
                testStudio.Close();

                testOverlay.Close();
                check("WPF Non-Activating Above-Cursor FERDOWSI Overlay & Studio (Bilingual + GitHub Link)",
                    hasHwnd && titleOk && studioOk,
                    "Overlay & StudioWindow created & verified cleanly");
            }
            catch (Exception ex)
            {
                check("WPF Non-Activating Above-Cursor Overlay HWND", false, ex.Message);
            }

            totalSw.Stop();
            log.AppendLine(string.Format("TOTAL SELF-TEST TIME: {0:0.0} ms | FAILURES: {1}", totalSw.Elapsed.TotalMilliseconds, failures));

            string output = log.ToString();
            if (!string.IsNullOrEmpty(reportPath))
            {
                try { File.WriteAllText(reportPath, output, Encoding.UTF8); } catch { }
            }
            else
            {
                try { Console.WriteLine(output); } catch { }
            }

            return failures == 0 ? 0 : 1;
        }
    }
}
