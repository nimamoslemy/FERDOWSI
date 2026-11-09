using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace GrammerV
{
    public class StudioWindow : Window
    {
        private readonly PythonEngineClient _engine;
        private readonly FloatingOverlay _overlay;
        private readonly Action _quitAppCallback;
        private bool _allowRealClose;

        private ScrollViewer _rootScroll;
        private Border _helpCard;
        private TextBox _playgroundBox;
        private TextBlock _studioStatusText;
        private Border _studioDictBorder;
        private StackPanel _studioDictStack;
        private TextBlock _phiStatusText;
        private CheckBox _chkFloating;
        private CheckBox _chkTypingTrigger;

        // Google Gemini, DeepSeek & Deepgram Voice Controls
        private ComboBox _cmbAiProvider;
        private ComboBox _cmbGoogleModel;
        private PasswordBox _pwdGoogleKey;
        private TextBox _txtGoogleKeyPlain;
        private bool _isKeyVisible;
        private Button _btnToggleKeyVis;
        private TextBlock _googleApiStatusText;

        private ComboBox _cmbDeepSeekModel;
        private PasswordBox _pwdDeepSeekKey;
        private TextBox _txtDeepSeekKeyPlain;
        private bool _isDeepSeekKeyVisible;
        private Button _btnToggleDeepSeekKeyVis;
        private TextBlock _deepSeekApiStatusText;

        private ComboBox _cmbDeepgramLang;
        private PasswordBox _pwdDeepgramKey;
        private TextBox _txtDeepgramKeyPlain;
        private bool _isDeepgramKeyVisible;
        private Button _btnToggleDeepgramKeyVis;
        private TextBlock _deepgramStatusText;

        private TextBlock _snoozeStatusText;

        private static readonly SolidColorBrush BgWindow = MakeBrush(11, 17, 32);
        private static readonly SolidColorBrush BgCard = MakeBrush(20, 29, 49);
        private static readonly SolidColorBrush BgInput = MakeBrush(15, 23, 42);
        private static readonly SolidColorBrush BgCombo = MakeBrush(241, 245, 249);
        private static readonly SolidColorBrush FgCombo = MakeBrush(15, 23, 42);
        private static readonly SolidColorBrush BgBtn = MakeBrush(51, 65, 85);
        private static readonly SolidColorBrush Emerald = MakeBrush(16, 185, 129);
        private static readonly SolidColorBrush EmeraldSoft = MakeBrush(110, 231, 183);
        private static readonly SolidColorBrush CoralSoft = MakeBrush(252, 165, 165);
        private static readonly SolidColorBrush TextLight = MakeBrush(248, 250, 252);
        private static readonly SolidColorBrush TextSub = MakeBrush(148, 163, 184);
        private static readonly SolidColorBrush BorderCol = MakeBrush(51, 65, 85);

        public StudioWindow(PythonEngineClient engine, FloatingOverlay overlay, Action quitAppCallback)
        {
            _engine = engine;
            _overlay = overlay;
            _quitAppCallback = quitAppCallback;

            Title = "FERDOWSI — Above-Cursor Writing Assistant & AI Settings";
            Width = 850;
            Height = 720;
            MinWidth = 680;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BgWindow;
            Icon = AppIconGenerator.CreateWpfIcon(64);

            BuildStudioUi();
            SourceInitialized += OnSourceInitialized;

            _engine.ConfigurationChanged += delegate
            {
                Dispatcher.BeginInvoke(new Action(RefreshRuntimeStatusLabels));
            };
            _overlay.SnoozeStateChanged += delegate
            {
                Dispatcher.BeginInvoke(new Action(RefreshRuntimeStatusLabels));
            };
        }

        public IntPtr Hwnd
        {
            get { return new WindowInteropHelper(this).Handle; }
        }

        public void AllowClose()
        {
            _allowRealClose = true;
        }

        public void ScrollToHelp()
        {
            if (_helpCard != null)
            {
                _helpCard.BringIntoView();
            }
            else if (_rootScroll != null)
            {
                _rootScroll.ScrollToBottom();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowRealClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            if (NativeBridge.IsNativeLoaded)
            {
                NativeBridge.gv_set_own_windows(_overlay.Hwnd.ToInt64(), Hwnd.ToInt64());
            }
        }

        private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
        {
            SolidColorBrush br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        private void BuildStudioUi()
        {
            _rootScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            StackPanel main = new StackPanel
            {
                Margin = new Thickness(22, 18, 22, 18)
            };

            // 1. Hero Header
            DockPanel header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };

            StackPanel titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            Border logo = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(11),
                Background = Emerald, Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            logo.Child = new TextBlock
            {
                Text = "F", Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(logo);
            titleRow.Children.Add(AppIconGenerator.CreateBrandFlagImage(37, new Thickness(0, 0, 12, 0)));

            StackPanel titleText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleText.Children.Add(new TextBlock
            {
                Text = "FERDOWSI", Foreground = TextLight, FontSize = 21, FontWeight = FontWeights.Bold
            });
            titleText.Children.Add(new TextBlock
            {
                Text = "System-wide Above-Cursor English, Persian & Finglish Writing Assistant (C++ · Python · C# · Gemini · DeepSeek · Deepgram Voice)",
                Foreground = TextSub, FontSize = 12
            });
            titleRow.Children.Add(titleText);
            DockPanel.SetDock(titleRow, Dock.Left);

            StackPanel topBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button helpBtn = MakeButton("❓ Help / راهنما", BgBtn, EmeraldSoft);
            helpBtn.Margin = new Thickness(0, 0, 8, 0);
            helpBtn.Click += delegate { ScrollToHelp(); };

            Button summonBtn = MakeButton("✨ Summon Popup (Alt+G)", Emerald, Brushes.White);
            summonBtn.Margin = new Thickness(0, 0, 8, 0);
            summonBtn.Click += delegate { _overlay.SummonAboveCursor(true); };

            Button quitBtn = MakeButton("Quit App", BgBtn, TextLight);
            quitBtn.Click += delegate { if (_quitAppCallback != null) _quitAppCallback(); };

            topBtns.Children.Add(helpBtn);
            topBtns.Children.Add(summonBtn);
            topBtns.Children.Add(quitBtn);
            DockPanel.SetDock(topBtns, Dock.Right);

            header.Children.Add(topBtns);
            header.Children.Add(titleRow);
            main.Children.Add(header);

            // 2. Engine Architecture Status Card
            Border archCard = MakeCard();
            StackPanel archStack = new StackPanel();
            archStack.Children.Add(new TextBlock
            {
                Text = "ACTIVE MULTI-LANGUAGE, CLOUD AI (GEMINI / DEEPSEEK), VOICE & NPU RUNTIME",
                Foreground = EmeraldSoft, FontSize = 11, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            WrapPanel pills = new WrapPanel();
            pills.Children.Add(MakeStatusPill("C++20 Caret, Mic & Password Shield DLL: " + (NativeBridge.IsNativeLoaded ? "Active ✓" : "Fallback")));
            pills.Children.Add(MakeStatusPill("Python Keep-Alive AI Engine: " + (_engine.IsPythonConnected ? ("v" + _engine.PythonVersion + " ✓") : "C# Fallback")));
            pills.Children.Add(MakeStatusPill("Password Field Auto-Shield: ON 🔒"));
            pills.Children.Add(MakeStatusPill("Hotkeys: Alt+G  /  Ctrl+Shift+G"));

            _phiStatusText = new TextBlock
            {
                Foreground = TextSub,
                FontSize = 11.5,
                Margin = new Thickness(2, 6, 0, 0)
            };

            archStack.Children.Add(pills);
            archStack.Children.Add(_phiStatusText);

            if (_engine.PhiSilicaState == 4)
            {
                StackPanel phiHintRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                Button unlockBtn = MakeButton("Unlock Phi Silica MSIX Identity", BgBtn, EmeraldSoft);
                unlockBtn.Padding = new Thickness(10, 4, 10, 4);
                unlockBtn.Click += OnUnlockPhiSilicaClicked;
                phiHintRow.Children.Add(unlockBtn);
                phiHintRow.Children.Add(new TextBlock
                {
                    Text = "Copilot+ NPU detected! Registers Sparse MSIX systemAIModels identity for hardware NPU rewrites.",
                    Foreground = TextSub, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                });
                archStack.Children.Add(phiHintRow);
            }

            archCard.Child = archStack;
            main.Children.Add(archCard);

            // 3. Cloud AI & Deepgram Voice Settings Card (Google Gemini + DeepSeek + Deepgram Voice + AI Only Mode)
            Border aiCard = MakeCard();
            StackPanel aiStack = new StackPanel();
            aiStack.Children.Add(new TextBlock
            {
                Text = "CLOUD AI & VOICE API SETTINGS — 1. GOOGLE GEMINI  ·  2. DEEPSEEK  ·  3. 🎤 DEEPGRAM AURA VOICE",
                Foreground = EmeraldSoft, FontSize = 11.5, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            aiStack.Children.Add(new TextBlock
            {
                Text = "Enter your API keys below for Google Gemini, DeepSeek, and 🎤 Deepgram Voice Dictation. Choose 'AI Only' so FERDOWSI uses exclusively Cloud AI (<0.8s Keep-Alive) and never falls back to Python offline rules.",
                Foreground = TextSub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Row A: AI Provider Priority (Full width)
            aiStack.Children.Add(new TextBlock
            {
                Text = "AI Provider & Source Mode", Foreground = TextLight, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _cmbAiProvider = new ComboBox
            {
                Background = BgCombo,
                Foreground = FgCombo,
                FontSize = 12.5,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 10)
            };
            AddProviderItem("ai_only", "AI Only (Cloud AI Only — Google Gemini / DeepSeek, Never Python Offline)");
            AddProviderItem("auto", "Auto (Google Gemini / DeepSeek → Phi Silica / Local AI → Offline Hybrid)");
            AddProviderItem("google", "Google Gemini API Preferred (Fastest <0.8s Flash-Lite Grammar & Rewording)");
            AddProviderItem("deepseek", "DeepSeek API Preferred (DeepSeek-V3 / DeepSeek-R1 Cloud AI)");
            AddProviderItem("local", "Local AI Only (Phi Silica NPU / LM Studio / Ollama)");
            AddProviderItem("offline", "Offline Fast Engine Only (Python + C++ Built-in)");
            SelectProviderInCombo(_engine.Settings.AiProvider);
            aiStack.Children.Add(_cmbAiProvider);

            // Row B: Google Gemini API Key + Model
            Grid geminiGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            geminiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star) });
            geminiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
            geminiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            StackPanel gKeyCol = new StackPanel();
            gKeyCol.Children.Add(new TextBlock
            {
                Text = "Google Gemini API Key (Google AI Studio)", Foreground = TextLight, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            DockPanel keyRow = new DockPanel();
            StackPanel keyBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 0, 0, 0)
            };
            _btnToggleKeyVis = MakeButton("Show", BgBtn, TextLight);
            _btnToggleKeyVis.Margin = new Thickness(0, 0, 5, 0);
            _btnToggleKeyVis.Click += OnToggleKeyVisibilityClicked;

            Button btnPasteKey = MakeButton("Paste", BgBtn, TextLight);
            btnPasteKey.Margin = new Thickness(0, 0, 5, 0);
            btnPasteKey.Click += delegate
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        SetEnteredGoogleKey(Clipboard.GetText().Trim());
                        _googleApiStatusText.Text = "Pasted Google API key — click '⚡ Save & Test Google Gemini' to activate.";
                        _googleApiStatusText.Foreground = EmeraldSoft;
                    }
                }
                catch { }
            };

            Button btnGetKey = MakeButton("Get Gemini Key ↗", BgBtn, EmeraldSoft);
            btnGetKey.Click += delegate
            {
                try { Process.Start("https://aistudio.google.com/apikey"); } catch { }
            };

            keyBtns.Children.Add(_btnToggleKeyVis);
            keyBtns.Children.Add(btnPasteKey);
            keyBtns.Children.Add(btnGetKey);
            DockPanel.SetDock(keyBtns, Dock.Right);

            Grid keyInputHost = new Grid();
            _pwdGoogleKey = new PasswordBox
            {
                Background = BgInput,
                Foreground = TextLight,
                CaretBrush = EmeraldSoft,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 13,
                Password = _engine.Settings.GoogleApiKey ?? string.Empty
            };
            _txtGoogleKeyPlain = new TextBox
            {
                Background = BgInput,
                Foreground = TextLight,
                CaretBrush = EmeraldSoft,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 13,
                Visibility = Visibility.Collapsed,
                Text = _engine.Settings.GoogleApiKey ?? string.Empty
            };
            keyInputHost.Children.Add(_pwdGoogleKey);
            keyInputHost.Children.Add(_txtGoogleKeyPlain);
            keyRow.Children.Add(keyBtns);
            keyRow.Children.Add(keyInputHost);
            gKeyCol.Children.Add(keyRow);
            Grid.SetColumn(gKeyCol, 0);
            geminiGrid.Children.Add(gKeyCol);

            StackPanel modelCol = new StackPanel();
            modelCol.Children.Add(new TextBlock
            {
                Text = "Google Gemini Model (Fastest: flash-lite)", Foreground = TextLight, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _cmbGoogleModel = new ComboBox
            {
                IsEditable = true,
                Background = BgCombo,
                Foreground = FgCombo,
                FontSize = 12.5,
                Padding = new Thickness(8, 5, 8, 5)
            };
            _cmbGoogleModel.Items.Add("gemini-3.1-flash-lite-preview");
            _cmbGoogleModel.Items.Add("gemini-flash-lite-latest");
            _cmbGoogleModel.Items.Add("gemini-3-flash-preview");
            _cmbGoogleModel.Items.Add("gemini-3.5-flash-lite");
            _cmbGoogleModel.Text = string.IsNullOrEmpty(_engine.Settings.GoogleModel) ? "gemini-3.1-flash-lite-preview" : _engine.Settings.GoogleModel;
            modelCol.Children.Add(_cmbGoogleModel);
            Grid.SetColumn(modelCol, 2);
            geminiGrid.Children.Add(modelCol);

            aiStack.Children.Add(geminiGrid);

            // Row C: DeepSeek API Key + Model
            Grid deepSeekGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            deepSeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star) });
            deepSeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
            deepSeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            StackPanel dsKeyCol = new StackPanel();
            dsKeyCol.Children.Add(new TextBlock
            {
                Text = "DeepSeek API Key (platform.deepseek.com)", Foreground = TextLight, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            DockPanel dsKeyRow = new DockPanel();
            StackPanel dsKeyBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 0, 0, 0)
            };
            _btnToggleDeepSeekKeyVis = MakeButton("Show", BgBtn, TextLight);
            _btnToggleDeepSeekKeyVis.Margin = new Thickness(0, 0, 5, 0);
            _btnToggleDeepSeekKeyVis.Click += OnToggleDeepSeekKeyVisibilityClicked;

            Button btnPasteDsKey = MakeButton("Paste", BgBtn, TextLight);
            btnPasteDsKey.Margin = new Thickness(0, 0, 5, 0);
            btnPasteDsKey.Click += delegate
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        SetEnteredDeepSeekKey(Clipboard.GetText().Trim());
                        _deepSeekApiStatusText.Text = "Pasted DeepSeek API key — click '⚡ Save & Test DeepSeek API' to activate.";
                        _deepSeekApiStatusText.Foreground = EmeraldSoft;
                    }
                }
                catch { }
            };

            Button btnGetDsKey = MakeButton("Get DeepSeek Key ↗", BgBtn, EmeraldSoft);
            btnGetDsKey.Click += delegate
            {
                try { Process.Start("https://platform.deepseek.com/api_keys"); } catch { }
            };

            dsKeyBtns.Children.Add(_btnToggleDeepSeekKeyVis);
            dsKeyBtns.Children.Add(btnPasteDsKey);
            dsKeyBtns.Children.Add(btnGetDsKey);
            DockPanel.SetDock(dsKeyBtns, Dock.Right);

            Grid dsKeyInputHost = new Grid();
            _pwdDeepSeekKey = new PasswordBox
            {
                Background = BgInput,
                Foreground = TextLight,
                CaretBrush = EmeraldSoft,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 13,
                Password = _engine.Settings.DeepSeekApiKey ?? string.Empty
            };
            _txtDeepSeekKeyPlain = new TextBox
            {
                Background = BgInput,
                Foreground = TextLight,
                CaretBrush = EmeraldSoft,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 13,
                Visibility = Visibility.Collapsed,
                Text = _engine.Settings.DeepSeekApiKey ?? string.Empty
            };
            dsKeyInputHost.Children.Add(_pwdDeepSeekKey);
            dsKeyInputHost.Children.Add(_txtDeepSeekKeyPlain);
            dsKeyRow.Children.Add(dsKeyBtns);
            dsKeyRow.Children.Add(dsKeyInputHost);
            dsKeyCol.Children.Add(dsKeyRow);
            Grid.SetColumn(dsKeyCol, 0);
            deepSeekGrid.Children.Add(dsKeyCol);

            StackPanel dsModelCol = new StackPanel();
            dsModelCol.Children.Add(new TextBlock
            {
                Text = "DeepSeek Model", Foreground = TextLight, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _cmbDeepSeekModel = new ComboBox
            {
                IsEditable = true,
                Background = BgCombo,
                Foreground = FgCombo,
                FontSize = 12.5,
                Padding = new Thickness(8, 5, 8, 5)
            };
            _cmbDeepSeekModel.Items.Add("deepseek-chat");
            _cmbDeepSeekModel.Items.Add("deepseek-reasoner");
            _cmbDeepSeekModel.Text = string.IsNullOrEmpty(_engine.Settings.DeepSeekModel) ? "deepseek-chat" : _engine.Settings.DeepSeekModel;
            dsModelCol.Children.Add(_cmbDeepSeekModel);
            Grid.SetColumn(dsModelCol, 2);
            deepSeekGrid.Children.Add(dsModelCol);

            aiStack.Children.Add(deepSeekGrid);

            // Row D: 🎤 Deepgram Voice / Aura Speech-to-Text API Key + Language (Right inside the top AI Card!)
            Border dgHighlightBox = new Border
            {
                Background = BgInput,
                BorderBrush = Emerald,
                BorderThickness = new Thickness(1.3),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StackPanel dgInnerStack = new StackPanel();
            dgInnerStack.Children.Add(new TextBlock
            {
                Text = "🎤 Deepgram Aura / Voice API Key — Write with Your Voice Above the Cursor (console.deepgram.com)",
                Foreground = EmeraldSoft,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            Grid dgGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            dgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star) });
            dgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
            dgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            StackPanel dgKeyCol = new StackPanel();
            dgKeyCol.Children.Add(new TextBlock
            {
                Text = "Deepgram API Key (Paste Here)", Foreground = TextLight, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            DockPanel dgKeyRow = new DockPanel();
            StackPanel dgKeyBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 0, 0, 0)
            };
            _btnToggleDeepgramKeyVis = MakeButton("Show", BgBtn, TextLight);
            _btnToggleDeepgramKeyVis.Margin = new Thickness(0, 0, 5, 0);
            _btnToggleDeepgramKeyVis.Click += OnToggleDeepgramKeyVisibilityClicked;

            Button btnPasteDgKey = MakeButton("Paste", BgBtn, TextLight);
            btnPasteDgKey.Margin = new Thickness(0, 0, 5, 0);
            btnPasteDgKey.Click += delegate
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        SetEnteredDeepgramKey(Clipboard.GetText().Trim());
                        _deepgramStatusText.Text = "Pasted Deepgram API key — click '🎤 Save & Test Deepgram Voice' below to activate!";
                        _deepgramStatusText.Foreground = EmeraldSoft;
                    }
                }
                catch { }
            };

            Button btnGetDgKey = MakeButton("Get Deepgram Key ↗", BgBtn, EmeraldSoft);
            btnGetDgKey.Click += delegate
            {
                try { Process.Start("https://console.deepgram.com/"); } catch { }
            };

            dgKeyBtns.Children.Add(_btnToggleDeepgramKeyVis);
            dgKeyBtns.Children.Add(btnPasteDgKey);
            dgKeyBtns.Children.Add(btnGetDgKey);
            DockPanel.SetDock(dgKeyBtns, Dock.Right);

            Grid dgKeyInputHost = new Grid();
            _pwdDeepgramKey = new PasswordBox
            {
                Background = BgCard,
                Foreground = TextLight,
                CaretBrush = EmeraldSoft,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 13,
                Password = _engine.Settings.DeepgramApiKey ?? string.Empty
            };
            _txtDeepgramKeyPlain = new TextBox
            {
                Background = BgCard,
                Foreground = TextLight,
                CaretBrush = EmeraldSoft,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 13,
                Visibility = Visibility.Collapsed,
                Text = _engine.Settings.DeepgramApiKey ?? string.Empty
            };
            dgKeyInputHost.Children.Add(_pwdDeepgramKey);
            dgKeyInputHost.Children.Add(_txtDeepgramKeyPlain);
            dgKeyRow.Children.Add(dgKeyBtns);
            dgKeyRow.Children.Add(dgKeyInputHost);
            dgKeyCol.Children.Add(dgKeyRow);
            Grid.SetColumn(dgKeyCol, 0);
            dgGrid.Children.Add(dgKeyCol);

            StackPanel dgLangCol = new StackPanel();
            dgLangCol.Children.Add(new TextBlock
            {
                Text = "Voice Dictation Language", Foreground = TextLight, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _cmbDeepgramLang = new ComboBox
            {
                Background = BgCombo,
                Foreground = FgCombo,
                FontSize = 12.5,
                Padding = new Thickness(8, 5, 8, 5)
            };
            AddDeepgramLangItem("multi", "Multi-Language Auto (English & International)");
            AddDeepgramLangItem("en", "English (en-US · Nova-2 Smart Punctuation)");
            AddDeepgramLangItem("fa", "Persian / فارسی (fa)");
            SelectDeepgramLangInCombo(_engine.Settings.DeepgramLanguage);
            dgLangCol.Children.Add(_cmbDeepgramLang);
            Grid.SetColumn(dgLangCol, 2);
            dgGrid.Children.Add(dgLangCol);

            dgInnerStack.Children.Add(dgGrid);

            _deepgramStatusText = new TextBlock
            {
                Text = _engine.HasDeepgramKey
                    ? "✓ Deepgram Voice API key configured! Click the 🎤 button on the floating window above your cursor to speak and write."
                    : "Paste your Deepgram API key here (click 'Get Deepgram Key ↗' for $200 free credit) and click '🎤 Save & Test Deepgram Voice'.",
                Foreground = _engine.HasDeepgramKey ? EmeraldSoft : TextSub,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            };
            dgInnerStack.Children.Add(_deepgramStatusText);
            dgHighlightBox.Child = dgInnerStack;
            aiStack.Children.Add(dgHighlightBox);

            // Row E: Save & Test Buttons + Live Status
            WrapPanel aiActionRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            Button btnSaveAi = MakeButton("💾 Save All Keys & Settings", Emerald, Brushes.White);
            btnSaveAi.Margin = new Thickness(0, 0, 8, 4);
            btnSaveAi.Click += OnSaveGoogleSettingsClicked;

            Button btnTestAi = MakeButton("⚡ Save & Test Google Gemini", BgBtn, EmeraldSoft);
            btnTestAi.Margin = new Thickness(0, 0, 8, 4);
            btnTestAi.Click += OnTestGoogleSettingsClicked;

            Button btnTestDs = MakeButton("⚡ Save & Test DeepSeek API", BgBtn, EmeraldSoft);
            btnTestDs.Margin = new Thickness(0, 0, 8, 4);
            btnTestDs.Click += OnTestDeepSeekSettingsClicked;

            Button btnTestDg = MakeButton("🎤 Save & Test Deepgram Voice", BgBtn, EmeraldSoft);
            btnTestDg.Margin = new Thickness(0, 0, 8, 4);
            btnTestDg.Click += OnTestDeepgramSettingsClicked;

            Button btnAiOnlyMode = MakeButton("✨ Use AI as Only Source (AI Only)", BgBtn, EmeraldSoft);
            btnAiOnlyMode.Margin = new Thickness(0, 0, 8, 4);
            btnAiOnlyMode.Click += delegate
            {
                SelectProviderInCombo("ai_only");
                OnSaveGoogleSettingsClicked(null, null);
            };

            Button btnClearKey = MakeButton("Clear Keys", BgBtn, TextSub);
            btnClearKey.Margin = new Thickness(0, 0, 8, 4);
            btnClearKey.Click += delegate
            {
                SetEnteredGoogleKey(string.Empty);
                SetEnteredDeepSeekKey(string.Empty);
                SetEnteredDeepgramKey(string.Empty);
                _engine.ApplyFullAiConfiguration(
                    string.Empty, GetSelectedGoogleModel(),
                    string.Empty, GetSelectedDeepSeekModel(),
                    string.Empty, GetSelectedDeepgramLang(),
                    GetSelectedAiProvider(), true);
                _googleApiStatusText.Text = "Cleared Cloud AI & Voice keys.";
                _googleApiStatusText.Foreground = TextSub;
                _deepSeekApiStatusText.Text = "";
                _deepgramStatusText.Text = "Cleared Deepgram Voice API key.";
                _deepgramStatusText.Foreground = TextSub;
            };

            aiActionRow.Children.Add(btnSaveAi);
            aiActionRow.Children.Add(btnTestAi);
            aiActionRow.Children.Add(btnTestDs);
            aiActionRow.Children.Add(btnTestDg);
            aiActionRow.Children.Add(btnAiOnlyMode);
            aiActionRow.Children.Add(btnClearKey);
            aiStack.Children.Add(aiActionRow);

            _googleApiStatusText = new TextBlock
            {
                Text = _engine.HasGoogleKey
                    ? ("✓ Google Gemini API key configured (" + _engine.Settings.GoogleModel + ", Keep-Alive + 0-Thinking fast mode active).")
                    : "Google Gemini API key not set — click 'Get Gemini Key ↗' for a free Google AI Studio key.",
                Foreground = _engine.HasGoogleKey ? EmeraldSoft : TextSub,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            };
            aiStack.Children.Add(_googleApiStatusText);

            _deepSeekApiStatusText = new TextBlock
            {
                Text = _engine.HasDeepSeekKey
                    ? ("✓ DeepSeek API key configured (" + _engine.Settings.DeepSeekModel + ").")
                    : "DeepSeek API key optional — click 'Get DeepSeek Key ↗' to use DeepSeek-V3 / R1.",
                Foreground = _engine.HasDeepSeekKey ? EmeraldSoft : TextSub,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            aiStack.Children.Add(_deepSeekApiStatusText);

            aiCard.Child = aiStack;
            main.Children.Add(aiCard);

            // 4. Behavior, Password Shield & Temporary Pause (10 min / 30 min / 1 hr) Card
            Border settingsCard = MakeCard();
            StackPanel setStack = new StackPanel();
            setStack.Children.Add(new TextBlock
            {
                Text = "ABOVE-CURSOR WIDGET, PASSWORD PROTECTION & PAUSE TIMER",
                Foreground = EmeraldSoft, FontSize = 11, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            Border pwdShieldBanner = new Border
            {
                Background = BgInput,
                BorderBrush = Emerald,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            pwdShieldBanner.Child = new TextBlock
            {
                Text = "🔒 Automatic Password Protection Active: FERDOWSI detects password inputs across browsers and Windows apps, immediately clears its typing buffer, and hides the floating window so your password is never revealed.",
                Foreground = EmeraldSoft,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            };
            setStack.Children.Add(pwdShieldBanner);

            _chkFloating = MakeCheckBox("Show FERDOWSI floating widget above my cursor anywhere in Windows (Edge, Chrome, Notepad, Office, etc.)", _overlay.FloatingEnabled);
            _chkFloating.Checked += delegate
            {
                _overlay.FloatingEnabled = true;
                _engine.Settings.FloatingEnabled = true;
                _engine.Settings.Save();
                RefreshRuntimeStatusLabels();
            };
            _chkFloating.Unchecked += delegate
            {
                _overlay.FloatingEnabled = false;
                _engine.Settings.FloatingEnabled = false;
                _engine.Settings.Save();
                RefreshRuntimeStatusLabels();
            };

            _chkTypingTrigger = MakeCheckBox("Appear automatically when typing or clicking into any text field (uncheck for selection / Alt+G only)", _overlay.ShowOnTypingAndFocus);
            _chkTypingTrigger.Checked += delegate
            {
                _overlay.ShowOnTypingAndFocus = true;
                _engine.Settings.ShowOnTypingAndFocus = true;
                _engine.Settings.Save();
            };
            _chkTypingTrigger.Unchecked += delegate
            {
                _overlay.ShowOnTypingAndFocus = false;
                _engine.Settings.ShowOnTypingAndFocus = false;
                _engine.Settings.Save();
            };

            setStack.Children.Add(_chkFloating);
            setStack.Children.Add(_chkTypingTrigger);

            // Quick Pause / Close Timer Bar (10 min, 30 min, 1 hr, Resume)
            WrapPanel snoozeBar = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
            Button btnPause10 = MakeButton("⏸ Close for 10 min", BgBtn, EmeraldSoft);
            btnPause10.Margin = new Thickness(0, 0, 6, 4);
            btnPause10.Click += delegate { _overlay.SnoozeForMinutes(10); };

            Button btnPause30 = MakeButton("⏸ Close for 30 min", BgBtn, EmeraldSoft);
            btnPause30.Margin = new Thickness(0, 0, 6, 4);
            btnPause30.Click += delegate { _overlay.SnoozeForMinutes(30); };

            Button btnPause60 = MakeButton("⏸ Close for 1 hour", BgBtn, EmeraldSoft);
            btnPause60.Margin = new Thickness(0, 0, 6, 4);
            btnPause60.Click += delegate { _overlay.SnoozeForMinutes(60); };

            Button btnResumeNow = MakeButton("▶ Resume Widget Now", Emerald, Brushes.White);
            btnResumeNow.Margin = new Thickness(0, 0, 8, 4);
            btnResumeNow.Click += delegate
            {
                _overlay.ResumeFromSnooze();
                _chkFloating.IsChecked = true;
                RefreshRuntimeStatusLabels();
            };

            snoozeBar.Children.Add(btnPause10);
            snoozeBar.Children.Add(btnPause30);
            snoozeBar.Children.Add(btnPause60);
            snoozeBar.Children.Add(btnResumeNow);
            setStack.Children.Add(snoozeBar);

            _snoozeStatusText = new TextBlock
            {
                Text = "Widget Status: " + _overlay.GetSnoozeStatusLabel() + "  (Tip: Click '×' on the small cursor window anytime to pause for 10 min, 30 min, or 1 hr)",
                Foreground = TextSub,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            };
            setStack.Children.Add(_snoozeStatusText);

            settingsCard.Child = setStack;
            main.Children.Add(settingsCard);

            RefreshRuntimeStatusLabels();

            // 5. Interactive Live Test Playground
            Border playCard = MakeCard();
            StackPanel playStack = new StackPanel();

            DockPanel playHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            playHeader.Children.Add(new TextBlock
            {
                Text = "LIVE SANDBOX — TYPE OR HIGHLIGHT TEXT BELOW (OR TEST IN EDGE / NOTEPAD)",
                Foreground = EmeraldSoft, FontSize = 11, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });

            Button notepadBtn = MakeButton("Open Notepad to Test Externally ↗", BgBtn, TextLight);
            notepadBtn.HorizontalAlignment = HorizontalAlignment.Right;
            notepadBtn.Padding = new Thickness(10, 4, 10, 4);
            notepadBtn.Click += OnOpenNotepadTestClicked;
            DockPanel.SetDock(notepadBtn, Dock.Right);
            playHeader.Children.Insert(0, notepadBtn);
            playStack.Children.Add(playHeader);

            WrapPanel sampleBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            Button s1 = MakeButton("Sample 1: Full-Sentence Grammar", BgBtn, TextLight);
            s1.Margin = new Thickness(0, 0, 6, 4);
            s1.Click += delegate
            {
                _playgroundBox.Text = "It seems that it can not get the full sentence and a few words will be remained untouched when it delete the sentence";
                _playgroundBox.Focus();
                _playgroundBox.SelectAll();
            };

            Button s2 = MakeButton("Sample 2: Typos & Agreement", BgBtn, TextLight);
            s2.Margin = new Thickness(0, 0, 6, 4);
            s2.Click += delegate
            {
                _playgroundBox.Text = "i has went to teh store yesterday for buying a apple and make an app just like grammerly above my cursur if it requiered";
                _playgroundBox.Focus();
                _playgroundBox.SelectAll();
            };

            Button s3 = MakeButton("Sample 3: Informal -> Formal/Concise", BgBtn, TextLight);
            s3.Margin = new Thickness(0, 0, 6, 4);
            s3.Click += delegate
            {
                _playgroundBox.Text = "hey can u send me teh report right now cuz i really need to check a lot of stuff in order to fix the problem";
                _playgroundBox.Focus();
                _playgroundBox.SelectAll();
            };

            Button s4 = MakeButton("Sample 4: Bullet / Email Format", BgBtn, TextLight);
            s4.Margin = new Thickness(0, 0, 6, 4);
            s4.Click += delegate
            {
                _playgroundBox.Text = "build the c++ hook dll, connect the python nlp engine, and compile the standalone c# exe file";
                _playgroundBox.Focus();
                _playgroundBox.SelectAll();
            };

            Button s5 = MakeButton("Sample 5: Finglish → فارسی", Emerald, Brushes.White);
            s5.Margin = new Thickness(0, 0, 6, 4);
            s5.Click += delegate
            {
                _playgroundBox.Text = "salam halet chetore? man emrooz miram daneshgah";
                _playgroundBox.Focus();
                _playgroundBox.SelectAll();
            };

            Button s6 = MakeButton("Sample 6: اصلاح خطای فارسی", BgBtn, EmeraldSoft);
            s6.Margin = new Thickness(0, 0, 6, 4);
            s6.Click += delegate
            {
                _playgroundBox.Text = "صلام حال شما چطوره؟ من می روم به خانه ها و سپاسگذارم از لطفتان";
                _playgroundBox.Focus();
                _playgroundBox.SelectAll();
            };

            Button s7 = MakeButton("Sample 7: 📖 AI Dictionary", BgBtn, EmeraldSoft);
            s7.Margin = new Thickness(0, 0, 6, 4);
            s7.Click += async delegate
            {
                _playgroundBox.Text = "resilient";
                _playgroundBox.Focus();
                _playgroundBox.SelectAll();
                await RunStudioDictionaryAsync("resilient");
            };

            sampleBar.Children.Add(s1);
            sampleBar.Children.Add(s2);
            sampleBar.Children.Add(s3);
            sampleBar.Children.Add(s4);
            sampleBar.Children.Add(s5);
            sampleBar.Children.Add(s6);
            sampleBar.Children.Add(s7);
            playStack.Children.Add(sampleBar);

            _playgroundBox = new TextBox
            {
                Background = BgInput,
                Foreground = TextLight,
                CaretBrush = EmeraldSoft,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(12, 10, 12, 10),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Height = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "It seems that it can not get the full sentence and a few words will be remained untouched when it delete the sentence"
            };
            _playgroundBox.TextChanged += delegate
            {
                _playgroundBox.FlowDirection = IsPrimarilyPersian(_playgroundBox.Text)
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
            };
            playStack.Children.Add(_playgroundBox);

            // Direct Studio Quick Action Strip (with live ms benchmark readout!)
            WrapPanel directBar = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
            Button btnFix = MakeButton("✓ Correct / فارسی", Emerald, Brushes.White);
            btnFix.Margin = new Thickness(0, 0, 6, 4);
            btnFix.Click += async delegate
            {
                AnalyzeResponse r = await _engine.AnalyzeAsync(GetPlaygroundTargetText());
                ApplyPlaygroundResult(r.Corrected, string.Format("Corrected {0} issue(s) via {1} in {2:0.0} ms", r.IssueCount, r.Engine, r.ElapsedMs));
            };

            Button btnFinglish = MakeButton("فینگلیش → فارسی", BgBtn, EmeraldSoft);
            btnFinglish.Margin = new Thickness(0, 0, 6, 4);
            btnFinglish.Click += async delegate
            {
                TransformResponse r = await _engine.FormatAsync(GetPlaygroundTargetText(), "finglish_to_fa");
                ApplyPlaygroundResult(r.Result, string.Format("Converted Finglish → Persian via {0} in {1:0.0} ms", r.Engine, r.ElapsedMs));
            };

            Button btnFormal = MakeButton("✨ Reword: Formal", BgBtn, TextLight);
            btnFormal.Margin = new Thickness(0, 0, 6, 4);
            btnFormal.Click += async delegate
            {
                TransformResponse r = await _engine.RewordAsync(GetPlaygroundTargetText(), "formal", 0);
                ApplyPlaygroundResult(r.Result, string.Format("Reworded (Formal) via {0} in {1:0.0} ms", r.Engine, r.ElapsedMs));
            };

            Button btnConcise = MakeButton("✨ Reword: Concise", BgBtn, TextLight);
            btnConcise.Margin = new Thickness(0, 0, 6, 4);
            btnConcise.Click += async delegate
            {
                TransformResponse r = await _engine.RewordAsync(GetPlaygroundTargetText(), "concise", 0);
                ApplyPlaygroundResult(r.Result, string.Format("Reworded (Concise) via {0} in {1:0.0} ms", r.Engine, r.ElapsedMs));
            };

            Button btnCasual = MakeButton("✨ Reword: Casual", BgBtn, TextLight);
            btnCasual.Margin = new Thickness(0, 0, 6, 4);
            btnCasual.Click += async delegate
            {
                TransformResponse r = await _engine.RewordAsync(GetPlaygroundTargetText(), "casual", 0);
                ApplyPlaygroundResult(r.Result, string.Format("Reworded (Casual) via {0} in {1:0.0} ms", r.Engine, r.ElapsedMs));
            };

            Button btnBullets = MakeButton("• Format: Bullet List", BgBtn, TextLight);
            btnBullets.Margin = new Thickness(0, 0, 6, 4);
            btnBullets.Click += async delegate
            {
                TransformResponse r = await _engine.FormatAsync(GetPlaygroundTargetText(), "bullet_list");
                ApplyPlaygroundResult(r.Result, string.Format("Formatted (Bullet List) via {0} in {1:0.0} ms", r.Engine, r.ElapsedMs));
            };

            Button btnEmail = MakeButton("✉ Format: Email", BgBtn, TextLight);
            btnEmail.Margin = new Thickness(0, 0, 6, 4);
            btnEmail.Click += async delegate
            {
                TransformResponse r = await _engine.FormatAsync(GetPlaygroundTargetText(), "email_format");
                ApplyPlaygroundResult(r.Result, string.Format("Formatted (Email) via {0} in {1:0.0} ms", r.Engine, r.ElapsedMs));
            };

            Button btnDict = MakeButton("📖 AI Dictionary (Fa · Syn · Ant)", Emerald, Brushes.White);
            btnDict.Margin = new Thickness(0, 0, 6, 4);
            btnDict.Click += async delegate
            {
                await RunStudioDictionaryAsync(GetPlaygroundTargetText());
            };

            directBar.Children.Add(btnFix);
            directBar.Children.Add(btnFinglish);
            directBar.Children.Add(btnDict);
            directBar.Children.Add(btnFormal);
            directBar.Children.Add(btnConcise);
            directBar.Children.Add(btnCasual);
            directBar.Children.Add(btnBullets);
            directBar.Children.Add(btnEmail);
            playStack.Children.Add(directBar);

            _studioDictBorder = new Border
            {
                Background = BgInput,
                BorderBrush = Emerald,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 6, 0, 6),
                Visibility = Visibility.Collapsed
            };
            _studioDictStack = new StackPanel();
            _studioDictBorder.Child = _studioDictStack;
            playStack.Children.Add(_studioDictBorder);

            _studioStatusText = new TextBlock
            {
                Text = "Tip: Click anywhere in the text box, drag-select words, or press Alt+G in any app to see the floating widget right above your cursor! Closing this window keeps FERDOWSI running in your System Tray.",
                Foreground = TextSub,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            playStack.Children.Add(_studioStatusText);

            playCard.Child = playStack;
            main.Children.Add(playCard);

            // 6. Bilingual Help & User Guide Card (English & Persian / راهنمای فارسی و انگلیسی)
            _helpCard = MakeCard();
            StackPanel helpOuter = new StackPanel();
            helpOuter.Children.Add(new TextBlock
            {
                Text = "HELP & USER GUIDE  ·  راهنمای جامع استفاده و دریافت API (ENGLISH & فارسی)",
                Foreground = EmeraldSoft,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            Grid helpGrid = new Grid();
            helpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            helpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14, GridUnitType.Pixel) });
            helpGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            // Left Column: English Help
            Border enHelpBox = new Border
            {
                Background = BgInput,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13, 11, 13, 11)
            };
            StackPanel enStack = new StackPanel { FlowDirection = FlowDirection.LeftToRight };
            enStack.Children.Add(new TextBlock
            {
                Text = "📘 English User Guide & API Setup",
                Foreground = EmeraldSoft,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });
            enStack.Children.Add(MakeHelpItem(
                "1. Important: You Need an API Key for Cloud AI & Voice Features",
                "To unlock full-sentence AI grammar & rewording, AI-Only mode, Finglish sentence conversion, AI Dictionary, and Voice Writing, you need a personal API key:\n" +
                "• Google Gemini API (Free tier available): Click 'Get Gemini Key ↗' (https://aistudio.google.com/apikey), sign in with Google, click 'Create API Key', paste it above, and click '⚡ Save & Test Google Gemini'.\n" +
                "• DeepSeek API: Click 'Get DeepSeek Key ↗' (https://platform.deepseek.com/api_keys), create an API key, paste it into the DeepSeek box, and click '⚡ Save & Test DeepSeek API'.\n" +
                "• Deepgram Voice API ($200 free credit): Click 'Get Deepgram Key ↗' (https://console.deepgram.com/), create a key, and click '🎤 Save & Test Deepgram Voice'."));
            enStack.Children.Add(MakeHelpItem(
                "2. ✨ 'AI Only' Mode & Fast <0.8s AI",
                "In 'AI Provider & Source Mode', select 'AI Only (Cloud AI Only — Never Python Offline)' or click '✨ Use AI as Only Source'. FERDOWSI will exclusively use your Cloud AI (Gemini or DeepSeek) with persistent HTTP Keep-Alive and 0-thinking latency (~0.4–0.8s instead of 4s)."));
            enStack.Children.Add(MakeHelpItem(
                "3. 🎤 Write with Your Voice Above the Cursor",
                "Click the 🎤 microphone button right on the small floating window above your cursor (or in the expanded card). Speak naturally in English or Persian, then click 🔴 to stop — Deepgram transcribes your voice and types it directly at your cursor!"));
            enStack.Children.Add(MakeHelpItem(
                "4. 🔒 Automatic Password Protection & ⏸ Close for 10 Minutes",
                "• Password Shield: FERDOWSI automatically detects password fields across browsers and Windows apps, clears its typing buffer, and hides so your password is never revealed.\n" +
                "• Pause / Close Timer: Click '×' on the small cursor window (or use the buttons above) to dismiss for the current box or close for 10 min, 30 min, or 1 hour."));
            enStack.Children.Add(MakeHelpItem(
                "5. ✓ Correct, ✨ Reword, Aa Format & 📖 AI Dictionary",
                "Fixes English/Persian grammar and half-spaces (نیم‌فاصله), converts Finglish (\"salam halet chetore\" → \"سلام، حالت چطوره؟\"), rewrites in 6 tones, and shows Persian translations, Synonyms (Syn), Antonyms (Ant), and examples. Your API keys are saved only in %APPDATA%\\FERDOWSI\\settings.json."));
            enHelpBox.Child = enStack;
            Grid.SetColumn(enHelpBox, 0);
            helpGrid.Children.Add(enHelpBox);

            // Right Column: Persian Help (راهنمای فارسی)
            Border faHelpBox = new Border
            {
                Background = BgInput,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13, 11, 13, 11)
            };
            StackPanel faStack = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
            faStack.Children.Add(new TextBlock
            {
                Text = "📘 راهنمای جامع استفاده و دریافت کلید API (فارسی)",
                Foreground = EmeraldSoft,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });
            faStack.Children.Add(MakeHelpItem(
                "۱. مهم: نیاز به کلید API و روش دریافت آن (Gemini، DeepSeek و Deepgram)",
                "برای استفاده از هوش مصنوعی ابری، حالت «فقط هوش مصنوعی (AI Only)»، بازنویسی جملات، دیکشنری هوشمند و تایپ صوتی، به کلید API نیاز دارید:\n" +
                "• دریافت کلید رایگان Google Gemini: روی دکمه «Get Gemini Key ↗» (سایت aistudio.google.com/apikey) کلیک کنید، با اکانت گوگل وارد شوید، روی Create API Key بزنید و کلید را در کادر Gemini قرار داده و «⚡ Save & Test Google Gemini» را بزنید.\n" +
                "• دریافت کلید DeepSeek: روی «Get DeepSeek Key ↗» (سایت platform.deepseek.com/api_keys) کلیک کنید، کلید بسازید و در کادر DeepSeek ذخیره و تست نمایید.\n" +
                "• دریافت کلید تایپ صوتی Deepgram: روی «Get Deepgram Key ↗» (سایت console.deepgram.com با ۲۰۰ دلار اعتبار رایگان اولیه) کلیک کنید و کلید را در بخش Voice ذخیره کنید."));
            faStack.Children.Add(MakeHelpItem(
                "۲. حالت «فقط هوش مصنوعی (AI Only)» و سرعت بسیار بالا (زیر ۱ ثانیه)",
                "اگر می‌خواهید برنامه هیچ‌گاه از موتور پایتون آفلاین استفاده نکند، گزینه «AI Only» را انتخاب کنید یا دکمه «✨ Use AI as Only Source» را بزنید. با اتصال پایدار Keep-Alive و حذف تأخیر فکر کردن، پاسخ هوش مصنوعی در کسری از ثانیه آماده می‌شود."));
            faStack.Children.Add(MakeHelpItem(
                "۳. 🎤 تایپ با صدا (Write with Your Voice) بالای مکان‌نما",
                "روی آیکون میکروفون 🎤 در پنجره کوچک بالای مکان‌نما کلیک کنید، صحبت کنید و سپس روی 🔴 کلیک نمایید تا صدای شما با Deepgram به متن تبدیل شده و مستقیماً در محل مکان‌نما تایپ شود!"));
            faStack.Children.Add(MakeHelpItem(
                "۴. 🔒 محافظت خودکار از رمز عبور و ⏸ بستن موقت (۱۰ دقیقه)",
                "• امنیت رمز عبور: هنگام کلیک یا تایپ در کادرهای رمز عبور (Password)، فردوسی به طور خودکار غیرفعال و پنهان می‌شود تا رمز شما هرگز نمایش داده نشود.\n" +
                "• بستن موقت: با کلیک روی دکمه «×» در پنجره کوچک بالای مکان‌نما می‌توانید آن را برای ۱۰ دقیقه (10 min)، ۳۰ دقیقه یا ۱ ساعت متوقف کنید."));
            faStack.Children.Add(MakeHelpItem(
                "۵. اصلاح متن، فینگلیش به فارسی، بازنویسی و دیکشنری هوشمند",
                "اصلاح خطاهای انگلیسی و فارسی و نیم‌فاصله، تبدیل فینگلیش (salam halet chetore ← سلام، حالت چطوره؟)، ۶ لحن بازنویسی و دیکشنری جامع (ترجمه فارسی، مترادف، متضاد و مثال). کلیدهای شما فقط در %APPDATA%\\FERDOWSI\\settings.json ذخیره می‌شوند."));
            faHelpBox.Child = faStack;
            Grid.SetColumn(faHelpBox, 2);
            helpGrid.Children.Add(faHelpBox);

            helpOuter.Children.Add(helpGrid);
            _helpCard.Child = helpOuter;
            main.Children.Add(_helpCard);

            // 7. GitHub Page Footer Card (Bottom of Settings Page)
            Border footerCard = new Border
            {
                Background = BgCard,
                BorderBrush = Emerald,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 4)
            };
            DockPanel footerDock = new DockPanel();

            Button githubBtn = MakeButton("GitHub: https://github.com/nimamoslemy ↗", BgBtn, EmeraldSoft);
            githubBtn.VerticalAlignment = VerticalAlignment.Center;
            githubBtn.Click += delegate { OpenAuthorGitHub(); };
            DockPanel.SetDock(githubBtn, Dock.Right);
            footerDock.Children.Add(githubBtn);

            StackPanel authorStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            authorStack.Children.Add(new TextBlock
            {
                Text = "FERDOWSI — Open Source Project",
                Foreground = TextLight,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });

            TextBlock linkBlock = new TextBlock
            {
                FontSize = 12.5,
                Foreground = TextSub
            };
            linkBlock.Inlines.Add(new Run("GitHub Page: "));
            Hyperlink ghLink = new Hyperlink(new Run("https://github.com/nimamoslemy"));
            ghLink.Foreground = EmeraldSoft;
            ghLink.Click += delegate { OpenAuthorGitHub(); };
            linkBlock.Inlines.Add(ghLink);
            authorStack.Children.Add(linkBlock);

            footerDock.Children.Add(authorStack);
            footerCard.Child = footerDock;
            main.Children.Add(footerCard);

            _rootScroll.Content = main;
            Content = _rootScroll;
        }

        private static void OpenAuthorGitHub()
        {
            try
            {
                Process.Start("https://github.com/nimamoslemy");
            }
            catch { }
        }

        private StackPanel MakeHelpItem(string heading, string body)
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
            sp.Children.Add(new TextBlock
            {
                Text = heading,
                Foreground = TextLight,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            });
            sp.Children.Add(new TextBlock
            {
                Text = body,
                Foreground = TextSub,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            });
            return sp;
        }

        private void AddProviderItem(string key, string label)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Tag = key;
            item.Content = label;
            _cmbAiProvider.Items.Add(item);
        }

        private void SelectProviderInCombo(string providerKey)
        {
            if (_cmbAiProvider == null) return;
            string target = string.IsNullOrEmpty(providerKey) ? "auto" : providerKey.ToLowerInvariant();
            for (int i = 0; i < _cmbAiProvider.Items.Count; i++)
            {
                ComboBoxItem item = _cmbAiProvider.Items[i] as ComboBoxItem;
                if (item != null && item.Tag != null && item.Tag.ToString() == target)
                {
                    _cmbAiProvider.SelectedIndex = i;
                    return;
                }
            }
            _cmbAiProvider.SelectedIndex = 0;
        }

        private string GetSelectedAiProvider()
        {
            if (_cmbAiProvider != null && _cmbAiProvider.SelectedItem is ComboBoxItem)
            {
                ComboBoxItem item = (ComboBoxItem)_cmbAiProvider.SelectedItem;
                if (item.Tag != null) return item.Tag.ToString();
            }
            return "auto";
        }

        private string GetSelectedGoogleModel()
        {
            if (_cmbGoogleModel == null || string.IsNullOrEmpty(_cmbGoogleModel.Text))
            {
                return "gemini-3.1-flash-lite-preview";
            }
            string m = _cmbGoogleModel.Text.Trim();
            return m.Length > 0 ? m : "gemini-3.1-flash-lite-preview";
        }

        private string GetSelectedDeepSeekModel()
        {
            if (_cmbDeepSeekModel == null || string.IsNullOrEmpty(_cmbDeepSeekModel.Text))
            {
                return "deepseek-chat";
            }
            string m = _cmbDeepSeekModel.Text.Trim();
            return m.Length > 0 ? m : "deepseek-chat";
        }

        private void AddDeepgramLangItem(string key, string label)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Tag = key;
            item.Content = label;
            _cmbDeepgramLang.Items.Add(item);
        }

        private void SelectDeepgramLangInCombo(string langKey)
        {
            if (_cmbDeepgramLang == null) return;
            string target = string.IsNullOrEmpty(langKey) ? "multi" : langKey.ToLowerInvariant();
            for (int i = 0; i < _cmbDeepgramLang.Items.Count; i++)
            {
                ComboBoxItem item = _cmbDeepgramLang.Items[i] as ComboBoxItem;
                if (item != null && item.Tag != null && item.Tag.ToString() == target)
                {
                    _cmbDeepgramLang.SelectedIndex = i;
                    return;
                }
            }
            _cmbDeepgramLang.SelectedIndex = 0;
        }

        private string GetSelectedDeepgramLang()
        {
            if (_cmbDeepgramLang != null && _cmbDeepgramLang.SelectedItem is ComboBoxItem)
            {
                ComboBoxItem item = (ComboBoxItem)_cmbDeepgramLang.SelectedItem;
                if (item.Tag != null) return item.Tag.ToString();
            }
            return "multi";
        }

        private string GetEnteredGoogleKey()
        {
            if (_isKeyVisible)
            {
                return (_txtGoogleKeyPlain.Text ?? string.Empty).Trim();
            }
            return (_pwdGoogleKey.Password ?? string.Empty).Trim();
        }

        private void SetEnteredGoogleKey(string key)
        {
            string clean = (key ?? string.Empty).Trim();
            if (_pwdGoogleKey != null) _pwdGoogleKey.Password = clean;
            if (_txtGoogleKeyPlain != null) _txtGoogleKeyPlain.Text = clean;
        }

        private string GetEnteredDeepSeekKey()
        {
            if (_isDeepSeekKeyVisible)
            {
                return (_txtDeepSeekKeyPlain.Text ?? string.Empty).Trim();
            }
            return (_pwdDeepSeekKey.Password ?? string.Empty).Trim();
        }

        private void SetEnteredDeepSeekKey(string key)
        {
            string clean = (key ?? string.Empty).Trim();
            if (_pwdDeepSeekKey != null) _pwdDeepSeekKey.Password = clean;
            if (_txtDeepSeekKeyPlain != null) _txtDeepSeekKeyPlain.Text = clean;
        }

        private string GetEnteredDeepgramKey()
        {
            if (_isDeepgramKeyVisible)
            {
                return (_txtDeepgramKeyPlain.Text ?? string.Empty).Trim();
            }
            return (_pwdDeepgramKey.Password ?? string.Empty).Trim();
        }

        private void SetEnteredDeepgramKey(string key)
        {
            string clean = (key ?? string.Empty).Trim();
            if (_pwdDeepgramKey != null) _pwdDeepgramKey.Password = clean;
            if (_txtDeepgramKeyPlain != null) _txtDeepgramKeyPlain.Text = clean;
        }

        private void OnToggleKeyVisibilityClicked(object sender, RoutedEventArgs e)
        {
            if (_isKeyVisible)
            {
                _pwdGoogleKey.Password = _txtGoogleKeyPlain.Text;
                _txtGoogleKeyPlain.Visibility = Visibility.Collapsed;
                _pwdGoogleKey.Visibility = Visibility.Visible;
                _btnToggleKeyVis.Content = "Show";
                _isKeyVisible = false;
            }
            else
            {
                _txtGoogleKeyPlain.Text = _pwdGoogleKey.Password;
                _pwdGoogleKey.Visibility = Visibility.Collapsed;
                _txtGoogleKeyPlain.Visibility = Visibility.Visible;
                _btnToggleKeyVis.Content = "Hide";
                _isKeyVisible = true;
            }
        }

        private void OnToggleDeepSeekKeyVisibilityClicked(object sender, RoutedEventArgs e)
        {
            if (_isDeepSeekKeyVisible)
            {
                _pwdDeepSeekKey.Password = _txtDeepSeekKeyPlain.Text;
                _txtDeepSeekKeyPlain.Visibility = Visibility.Collapsed;
                _pwdDeepSeekKey.Visibility = Visibility.Visible;
                _btnToggleDeepSeekKeyVis.Content = "Show";
                _isDeepSeekKeyVisible = false;
            }
            else
            {
                _txtDeepSeekKeyPlain.Text = _pwdDeepSeekKey.Password;
                _pwdDeepSeekKey.Visibility = Visibility.Collapsed;
                _txtDeepSeekKeyPlain.Visibility = Visibility.Visible;
                _btnToggleDeepSeekKeyVis.Content = "Hide";
                _isDeepSeekKeyVisible = true;
            }
        }

        private void OnToggleDeepgramKeyVisibilityClicked(object sender, RoutedEventArgs e)
        {
            if (_isDeepgramKeyVisible)
            {
                _pwdDeepgramKey.Password = _txtDeepgramKeyPlain.Text;
                _txtDeepgramKeyPlain.Visibility = Visibility.Collapsed;
                _pwdDeepgramKey.Visibility = Visibility.Visible;
                _btnToggleDeepgramKeyVis.Content = "Show";
                _isDeepgramKeyVisible = false;
            }
            else
            {
                _txtDeepgramKeyPlain.Text = _pwdDeepgramKey.Password;
                _pwdDeepgramKey.Visibility = Visibility.Collapsed;
                _txtDeepgramKeyPlain.Visibility = Visibility.Visible;
                _btnToggleDeepgramKeyVis.Content = "Hide";
                _isDeepgramKeyVisible = true;
            }
        }

        private void OnSaveGoogleSettingsClicked(object sender, RoutedEventArgs e)
        {
            string gKey = GetEnteredGoogleKey();
            string gModel = GetSelectedGoogleModel();
            string dsKey = GetEnteredDeepSeekKey();
            string dsModel = GetSelectedDeepSeekModel();
            string dgKey = GetEnteredDeepgramKey();
            string dgLang = GetSelectedDeepgramLang();
            string provider = GetSelectedAiProvider();

            _engine.ApplyFullAiConfiguration(gKey, gModel, dsKey, dsModel, dgKey, dgLang, provider, true);

            if ((provider == "google" || provider == "ai_only") && string.IsNullOrEmpty(gKey) && string.IsNullOrEmpty(dsKey))
            {
                _googleApiStatusText.Text = string.Format("Saved mode '{0}', but no Cloud AI key is entered yet — paste a Google Gemini or DeepSeek API key above.", provider);
                _googleApiStatusText.Foreground = CoralSoft;
            }
            else if (!string.IsNullOrEmpty(gKey))
            {
                _googleApiStatusText.Text = string.Format("✓ Saved! Google Gemini ({0}) is active with provider mode '{1}'.", gModel, provider);
                _googleApiStatusText.Foreground = EmeraldSoft;
            }
            else
            {
                _googleApiStatusText.Text = string.Format("✓ Saved AI settings (provider mode: '{0}').", provider);
                _googleApiStatusText.Foreground = EmeraldSoft;
            }

            if (!string.IsNullOrEmpty(dsKey))
            {
                _deepSeekApiStatusText.Text = string.Format("✓ DeepSeek ({0}) key saved and ready.", dsModel);
                _deepSeekApiStatusText.Foreground = EmeraldSoft;
            }
            if (!string.IsNullOrEmpty(dgKey))
            {
                _deepgramStatusText.Text = string.Format("✓ Deepgram Voice key saved (language: '{0}'). Click 🎤 above your cursor to dictate!", dgLang);
                _deepgramStatusText.Foreground = EmeraldSoft;
            }
        }

        private async void OnTestGoogleSettingsClicked(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            string key = GetEnteredGoogleKey();
            string model = GetSelectedGoogleModel();
            string provider = GetSelectedAiProvider();

            if (string.IsNullOrEmpty(key))
            {
                _googleApiStatusText.Text = "Please enter or paste your Google Gemini API key first (click 'Get Gemini Key ↗' for a free Google AI Studio key).";
                _googleApiStatusText.Foreground = CoralSoft;
                return;
            }

            if (provider == "offline" || provider == "local")
            {
                provider = "ai_only";
                SelectProviderInCombo("ai_only");
            }

            _engine.ApplyFullAiConfiguration(
                key, model,
                GetEnteredDeepSeekKey(), GetSelectedDeepSeekModel(),
                GetEnteredDeepgramKey(), GetSelectedDeepgramLang(),
                provider, true);

            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "Testing Gemini...";
            }
            _googleApiStatusText.Text = string.Format("Connecting to Google Gemini ({0})...", model);
            _googleApiStatusText.Foreground = TextSub;

            try
            {
                GoogleTestResult res = await _engine.TestGoogleApiAsync(key, model);
                if (res.Ok)
                {
                    _googleApiStatusText.Text = string.Format("✓ Google Gemini ({0}) connected in {1:0.0} ms! Output: \"{2}\"",
                        model, res.ElapsedMs, res.SampleOutput);
                    _googleApiStatusText.Foreground = EmeraldSoft;
                }
                else
                {
                    _googleApiStatusText.Text = "✗ Google Gemini test failed: " + res.Error;
                    _googleApiStatusText.Foreground = CoralSoft;
                }
            }
            finally
            {
                if (btn != null)
                {
                    btn.Content = "⚡ Save & Test Google Gemini";
                    btn.IsEnabled = true;
                }
            }
        }

        private async void OnTestDeepSeekSettingsClicked(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            string dsKey = GetEnteredDeepSeekKey();
            string dsModel = GetSelectedDeepSeekModel();
            string provider = GetSelectedAiProvider();

            if (string.IsNullOrEmpty(dsKey))
            {
                _deepSeekApiStatusText.Text = "Please enter or paste your DeepSeek API key first (click 'Get DeepSeek Key ↗').";
                _deepSeekApiStatusText.Foreground = CoralSoft;
                return;
            }

            if (provider == "offline" || provider == "local" || (provider == "auto" && !_engine.HasGoogleKey))
            {
                provider = "deepseek";
                SelectProviderInCombo("deepseek");
            }

            _engine.ApplyFullAiConfiguration(
                GetEnteredGoogleKey(), GetSelectedGoogleModel(),
                dsKey, dsModel,
                GetEnteredDeepgramKey(), GetSelectedDeepgramLang(),
                provider, true);

            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "Testing DeepSeek...";
            }
            _deepSeekApiStatusText.Text = string.Format("Connecting to DeepSeek ({0})...", dsModel);
            _deepSeekApiStatusText.Foreground = TextSub;

            try
            {
                GoogleTestResult res = await _engine.TestDeepSeekApiAsync(dsKey, dsModel);
                if (res.Ok)
                {
                    _deepSeekApiStatusText.Text = string.Format("✓ DeepSeek ({0}) connected in {1:0.0} ms! Output: \"{2}\"",
                        dsModel, res.ElapsedMs, res.SampleOutput);
                    _deepSeekApiStatusText.Foreground = EmeraldSoft;
                }
                else
                {
                    _deepSeekApiStatusText.Text = "✗ DeepSeek test failed: " + res.Error;
                    _deepSeekApiStatusText.Foreground = CoralSoft;
                }
            }
            finally
            {
                if (btn != null)
                {
                    btn.Content = "⚡ Save & Test DeepSeek API";
                    btn.IsEnabled = true;
                }
            }
        }

        private async void OnTestDeepgramSettingsClicked(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            string dgKey = GetEnteredDeepgramKey();
            string dgLang = GetSelectedDeepgramLang();

            if (string.IsNullOrEmpty(dgKey))
            {
                _deepgramStatusText.Text = "Please enter or paste your Deepgram API key first (click 'Get Deepgram Key ↗').";
                _deepgramStatusText.Foreground = CoralSoft;
                return;
            }

            _engine.ApplyFullAiConfiguration(
                GetEnteredGoogleKey(), GetSelectedGoogleModel(),
                GetEnteredDeepSeekKey(), GetSelectedDeepSeekModel(),
                dgKey, dgLang,
                GetSelectedAiProvider(), true);

            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "Testing Deepgram...";
            }
            _deepgramStatusText.Text = "Verifying Deepgram Voice API key...";
            _deepgramStatusText.Foreground = TextSub;

            try
            {
                GoogleTestResult res = await _engine.TestDeepgramApiAsync(dgKey);
                if (res.Ok)
                {
                    _deepgramStatusText.Text = string.Format("✓ {0} ({1:0.0} ms)! Click 🎤 on the small cursor window anytime to write with your voice.",
                        res.SampleOutput, res.ElapsedMs);
                    _deepgramStatusText.Foreground = EmeraldSoft;
                }
                else
                {
                    _deepgramStatusText.Text = "✗ Deepgram test failed: " + res.Error;
                    _deepgramStatusText.Foreground = CoralSoft;
                }
            }
            finally
            {
                if (btn != null)
                {
                    btn.Content = "🎤 Save & Test Deepgram Voice";
                    btn.IsEnabled = true;
                }
            }
        }

        private void RefreshRuntimeStatusLabels()
        {
            if (_phiStatusText != null)
            {
                string geminiLabel = _engine.HasGoogleKey
                    ? ("ON (" + _engine.Settings.GoogleModel + ")")
                    : "Not Set";
                string dsLabel = _engine.HasDeepSeekKey
                    ? ("ON (" + _engine.Settings.DeepSeekModel + ")")
                    : "Not Set";
                string dgLabel = _engine.HasDeepgramKey ? "ON 🎤" : "Not Set";
                _phiStatusText.Text = string.Format("Gemini: {0}  |  DeepSeek: {1}  |  Deepgram Voice: {2}  |  AI Mode: {3}",
                    geminiLabel, dsLabel, dgLabel, _engine.Settings.AiProvider);
            }
            if (_snoozeStatusText != null && _overlay != null)
            {
                _snoozeStatusText.Text = "Widget Status: " + _overlay.GetSnoozeStatusLabel() +
                    "  (Tip: Click '×' on the small cursor window anytime to pause for 10 min, 30 min, or 1 hr)";
                _snoozeStatusText.Foreground = _overlay.IsSnoozed ? CoralSoft : EmeraldSoft;
            }
            if (_chkFloating != null && _overlay != null)
            {
                _chkFloating.IsChecked = _overlay.FloatingEnabled;
            }
            SelectProviderInCombo(_engine.Settings.AiProvider);
        }

        private string GetPlaygroundTargetText()
        {
            if (!string.IsNullOrEmpty(_playgroundBox.SelectedText))
            {
                return _playgroundBox.SelectedText;
            }
            return _playgroundBox.Text;
        }

        private void ApplyPlaygroundResult(string newText, string statusMsg)
        {
            if (!string.IsNullOrEmpty(_playgroundBox.SelectedText))
            {
                int start = _playgroundBox.SelectionStart;
                _playgroundBox.SelectedText = newText;
                _playgroundBox.Select(start, newText.Length);
            }
            else
            {
                _playgroundBox.Text = newText;
            }
            _studioStatusText.Text = "✓ " + statusMsg;
            _studioStatusText.Foreground = EmeraldSoft;
        }

        private async System.Threading.Tasks.Task RunStudioDictionaryAsync(string query)
        {
            if (string.IsNullOrEmpty(query) || query.Trim().Length == 0) return;
            _studioDictBorder.Visibility = Visibility.Visible;
            _studioDictStack.Children.Clear();
            _studioDictStack.Children.Add(new TextBlock
            {
                Text = "🤖 Querying AI Dictionary (Persian translations, synonyms, antonyms & example sentences)...",
                Foreground = EmeraldSoft,
                FontSize = 12.5
            });

            DictionaryResponse dr = await _engine.DictionaryLookupAsync(query.Trim());
            _studioDictStack.Children.Clear();

            DockPanel topRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            Button closeDictBtn = MakeButton("Hide ×", BgBtn, TextSub);
            closeDictBtn.Padding = new Thickness(8, 2, 8, 2);
            closeDictBtn.Click += delegate { _studioDictBorder.Visibility = Visibility.Collapsed; };
            DockPanel.SetDock(closeDictBtn, Dock.Right);
            topRow.Children.Add(closeDictBtn);

            WrapPanel headWrap = new WrapPanel();
            headWrap.Children.Add(new TextBlock
            {
                Text = "📖 " + (string.IsNullOrEmpty(dr.Headword) ? dr.Query : dr.Headword),
                Foreground = TextLight,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 8, 0)
            });
            if (!string.IsNullOrEmpty(dr.PartOfSpeech))
            {
                headWrap.Children.Add(new TextBlock
                {
                    Text = "[" + dr.PartOfSpeech + "]",
                    Foreground = EmeraldSoft,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });
            }
            if (!string.IsNullOrEmpty(dr.Phonetic))
            {
                headWrap.Children.Add(new TextBlock
                {
                    Text = dr.Phonetic,
                    Foreground = TextSub,
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            topRow.Children.Add(headWrap);
            _studioDictStack.Children.Add(topRow);

            if (!string.IsNullOrEmpty(dr.Definition))
            {
                _studioDictStack.Children.Add(new TextBlock
                {
                    Text = dr.Definition,
                    Foreground = TextSub,
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }

            if (!string.IsNullOrEmpty(dr.PersianTranslation))
            {
                _studioDictStack.Children.Add(new TextBlock
                {
                    Text = "ترجمه فارسی:  " + dr.PersianTranslation,
                    Foreground = EmeraldSoft,
                    FontSize = 14.5,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    FlowDirection = FlowDirection.RightToLeft,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }

            if (dr.Synonyms != null && dr.Synonyms.Count > 0)
            {
                _studioDictStack.Children.Add(new TextBlock
                {
                    Text = "Synonyms (مترادف‌ها):  " + string.Join("   ·   ", dr.Synonyms.ToArray()),
                    Foreground = TextLight,
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            if (dr.Antonyms != null && dr.Antonyms.Count > 0)
            {
                _studioDictStack.Children.Add(new TextBlock
                {
                    Text = "Antonyms (متضادها):  " + string.Join("   ·   ", dr.Antonyms.ToArray()),
                    Foreground = CoralSoft,
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }

            if (dr.Examples != null && dr.Examples.Count > 0)
            {
                _studioDictStack.Children.Add(new TextBlock
                {
                    Text = "Example Sentences (جمله‌های نمونه):",
                    Foreground = EmeraldSoft,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 2, 0, 3)
                });
                for (int i = 0; i < dr.Examples.Count; i++)
                {
                    DictionaryExampleItem ex = dr.Examples[i];
                    if (!string.IsNullOrEmpty(ex.En))
                    {
                        _studioDictStack.Children.Add(new TextBlock
                        {
                            Text = "• " + ex.En,
                            Foreground = TextLight,
                            FontSize = 12.5,
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                    if (!string.IsNullOrEmpty(ex.Fa))
                    {
                        _studioDictStack.Children.Add(new TextBlock
                        {
                            Text = ex.Fa,
                            Foreground = TextSub,
                            FontSize = 12.5,
                            TextWrapping = TextWrapping.Wrap,
                            FlowDirection = FlowDirection.RightToLeft,
                            Margin = new Thickness(0, 1, 0, 4)
                        });
                    }
                }
            }

            _studioStatusText.Text = string.Format("✓ AI Dictionary lookup completed via {0} in {1:0.0} ms", dr.Engine, dr.ElapsedMs);
            _studioStatusText.Foreground = EmeraldSoft;
        }

        private static bool IsPrimarilyPersian(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int faCount = 0;
            int latCount = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '\u0600' && c <= '\u06FF') || (c >= '\uFB50' && c <= '\uFEFF'))
                {
                    faCount++;
                }
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    latCount++;
                }
            }
            return faCount > 0 && faCount >= latCount;
        }

        private void OnOpenNotepadTestClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "FERDOWSI_TestPad.txt");
                File.WriteAllText(tempFile,
                    "Try FERDOWSI right here in Notepad (English, Finglish & Persian)!\r\n\r\n" +
                    "1. Select any sentence below or keep typing — notice the FERDOWSI badge appears right above your cursor!\r\n" +
                    "2. Or press Alt+G (or Ctrl+Shift+G) anytime.\r\n\r\n" +
                    "i has went to teh store yesterday for buying a apple and some informations\r\n\r\n" +
                    "salam halet chetore\r\n\r\n" +
                    "صلام من می روم به خانه ها و سپاسگذارم از لطفتان\r\n",
                    System.Text.Encoding.UTF8);
                Process.Start("notepad.exe", "\"" + tempFile + "\"");
            }
            catch (Exception ex)
            {
                _studioStatusText.Text = "Could not open Notepad: " + ex.Message;
            }
        }

        private void OnUnlockPhiSilicaClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "enable_phi_silica.ps1");
                if (File.Exists(script))
                {
                    ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                        "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"");
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                    _studioStatusText.Text = "Launched Phi Silica Sparse MSIX registration script. Restart FERDOWSI after enabling Developer Mode & registering.";
                }
                else
                {
                    Process.Start("ms-settings:developers");
                }
            }
            catch { }
        }

        private Border MakeCard()
        {
            return new Border
            {
                Background = BgCard,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 13, 15, 13),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private Border MakeStatusPill(string text)
        {
            Border b = new Border
            {
                Background = BgInput,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 8, 4)
            };
            b.Child = new TextBlock
            {
                Text = text,
                Foreground = TextLight,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold
            };
            return b;
        }

        private CheckBox MakeCheckBox(string label, bool isChecked)
        {
            CheckBox cb = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = label,
                    Foreground = TextLight,
                    FontSize = 12.5,
                    Margin = new Thickness(4, -1, 0, 0)
                },
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };
            return cb;
        }

        private Button MakeButton(string text, Brush bg, Brush fg)
        {
            Button btn = new Button
            {
                Content = text,
                Background = bg,
                Foreground = fg,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };

            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            btn.Template = tpl;
            return btn;
        }
    }
}
