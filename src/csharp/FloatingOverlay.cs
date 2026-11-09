using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace GrammerV
{
    public class FloatingOverlay : Window
    {
        private readonly PythonEngineClient _engine;
        private readonly Action _openStudioCallback;
        private readonly Action _openHelpCallback;
        private IntPtr _hwnd;
        private DispatcherTimer _pollTimer;
        private DispatcherTimer _aiPrefetchTimer;

        // Settings controlled from Studio / Tray
        public bool FloatingEnabled { get; set; }
        public bool ShowOnTypingAndFocus { get; set; }
        public int VerticalOffsetPx { get; set; }

        // Pause / Snooze support (e.g., 10 min, 30 min, 1 hr)
        private DateTime _snoozeUntilUtc;
        public event Action SnoozeStateChanged;

        // State tracking
        private bool _isExpanded;
        private bool _isCloseMenuOpen;
        private bool _dismissedForCurrentFocus;
        private long _lastForegroundHwnd;
        private long _activeTargetHwnd;
        private AutomationElement _activeUiaElement;
        private bool _activeSupportsValueSet;

        // Replace mode: 0=Selection(Ctrl+V), 1=Backspace N typed chars+Ctrl+V, 2=Line, 3=SelectAll+Ctrl+V, 4=UIA ValuePattern
        private int _replaceMode;
        private int _typedBackspaceCount;
        private string _sourceLabel;
        private string _activeSourceText;
        private string _currentOutputText;

        private string _lastAnalyzedText;
        private ulong _lastAnalyzedTypeTick;
        private bool _lastAnalysisWasFast;
        private int _analysisSeq;
        private int _pollTickCounter;
        private UiaInspectionResult _cachedUia;
        private DateTime _lastActivityUtc;

        // Voice dictation & Read Aloud state
        private bool _isVoiceRecording;
        private bool _voiceBusy;
        private bool _isReadingAloud;
        private System.Media.SoundPlayer _ttsPlayer;

        private int _lastMovedX;
        private int _lastMovedY;
        private int _anchorCaretX;
        private int _anchorCaretY;
        private int _anchorCaretH;

        private AnalyzeResponse _latestAnalysis;
        private DictionaryResponse _latestDict;
        private string _lastDictQuery;
        private bool _dictInFlight;
        private string _activeTab; // "correct", "reword", "format", "dictionary"
        private string _activeRewordMode;
        private int _rewordVariantIndex;
        private string _activeFormatMode;

        // UI Elements - Collapsed Pill
        private Border _pillRoot;
        private Border _closeMenuRoot;
        private Border _pillBadgeBorder;
        private TextBlock _pillBadgeText;
        private TextBlock _pillSummaryText;
        private Button _pillMicBtn;
        private Button _pillReadAloudBtn;
        private Button _pillQuickFixBtn;

        // UI Elements - Expanded Card
        private Border _cardRoot;
        private TextBlock _cardSourceBadge;
        private TextBlock _cardTimingBadge;
        private Button _cardMicBtn;
        private Button _cardReadAloudBtn;
        private Button _aiModeToggleBtn;
        private Button _tabCorrectBtn;
        private Button _tabRewordBtn;
        private Button _tabFormatBtn;
        private Button _tabDictBtn;
        private StackPanel _tabContentHost;
        private TextBlock _diffPreviewBlock;
        private WrapPanel _issuesWrap;
        private StackPanel _rewordSectionPanel;
        private WrapPanel _rewordChipsWrap;
        private TextBox _customRewordInputBox;
        private Button _customRewordApplyBtn;
        private bool _allowInputBoxFocus;
        private WrapPanel _formatChipsWrap;
        private TextBox _outputPreviewBox;
        private Border _dictBox;
        private StackPanel _dictContentStack;
        private Button _replaceBtn;
        private Button _copyBtn;

        // Brushes
        private static readonly SolidColorBrush BgDark = MakeBrush(15, 23, 42, 248);
        private static readonly SolidColorBrush BgSurface = MakeBrush(30, 41, 59, 255);
        private static readonly SolidColorBrush BgChip = MakeBrush(51, 65, 85, 255);
        private static readonly SolidColorBrush Emerald = MakeBrush(16, 185, 129, 255);
        private static readonly SolidColorBrush EmeraldDark = MakeBrush(6, 95, 70, 255);
        private static readonly SolidColorBrush EmeraldTint = MakeBrush(6, 78, 59, 135);
        private static readonly SolidColorBrush Coral = MakeBrush(239, 68, 68, 255);
        private static readonly SolidColorBrush CoralDark = MakeBrush(127, 29, 29, 190);
        private static readonly SolidColorBrush CoralSoft = MakeBrush(252, 165, 165, 255);
        private static readonly SolidColorBrush EmeraldSoft = MakeBrush(110, 231, 183, 255);
        private static readonly SolidColorBrush TextPrimary = MakeBrush(248, 250, 252, 255);
        private static readonly SolidColorBrush TextMuted = MakeBrush(148, 163, 184, 255);
        private static readonly SolidColorBrush BorderSubtle = MakeBrush(51, 65, 85, 220);

        public FloatingOverlay(PythonEngineClient engine, Action openStudioCallback)
            : this(engine, openStudioCallback, null)
        {
        }

        public FloatingOverlay(PythonEngineClient engine, Action openStudioCallback, Action openHelpCallback)
        {
            _engine = engine;
            _openStudioCallback = openStudioCallback;
            _openHelpCallback = openHelpCallback;

            FloatingEnabled = engine.Settings.FloatingEnabled;
            ShowOnTypingAndFocus = engine.Settings.ShowOnTypingAndFocus;
            VerticalOffsetPx = 12;

            _engine.ConfigurationChanged += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    UpdateAiButtonAppearance();
                    UpdateVoiceModeAppearance();
                    UpdateShortWindowButtonsVisibility();
                    if (_isExpanded && !string.IsNullOrEmpty(_activeSourceText))
                    {
                        TriggerAnalysis(_activeSourceText, true, false);
                    }
                }));
            };

            _sourceLabel = "Cursor";
            _activeSourceText = string.Empty;
            _currentOutputText = string.Empty;
            _lastAnalyzedText = null;
            _cachedUia = new UiaInspectionResult();
            _latestAnalysis = new AnalyzeResponse();
            _activeTab = "correct";
            _activeRewordMode = "fluent";
            _activeFormatMode = "sentence_case";
            _lastActivityUtc = DateTime.UtcNow;

            Title = "FERDOWSI Overlay";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Left = -2000;
            Top = -2000;

            BuildUi();

            SourceInitialized += OnSourceInitialized;
            Closed += OnOverlayClosed;
        }

        public IntPtr Hwnd
        {
            get { return _hwnd; }
        }

        public bool IsSnoozed
        {
            get { return DateTime.UtcNow < _snoozeUntilUtc; }
        }

        public void SnoozeForMinutes(int minutes)
        {
            _snoozeUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
            HideOverlay();
            if (SnoozeStateChanged != null) SnoozeStateChanged();
        }

        public void ResumeFromSnooze()
        {
            _snoozeUntilUtc = DateTime.MinValue;
            _dismissedForCurrentFocus = false;
            FloatingEnabled = true;
            _engine.Settings.FloatingEnabled = true;
            _engine.Settings.Save();
            if (SnoozeStateChanged != null) SnoozeStateChanged();
        }

        public string GetSnoozeStatusLabel()
        {
            if (!FloatingEnabled)
            {
                return "Disabled (turned off)";
            }
            if (IsSnoozed)
            {
                TimeSpan rem = _snoozeUntilUtc - DateTime.UtcNow;
                int mins = Math.Max(0, (int)rem.TotalMinutes);
                int secs = Math.Max(0, rem.Seconds);
                return string.Format("Paused ({0}m {1}s left)", mins, secs);
            }
            return "Active (watching cursor)";
        }

        private static SolidColorBrush MakeBrush(byte r, byte g, byte b, byte a)
        {
            SolidColorBrush br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeBridge.MakeWindowNonActivating(_hwnd);

            HwndSource src = HwndSource.FromHwnd(_hwnd);
            if (src != null)
            {
                src.AddHook(WndProc);
            }

            // Global Windows Shortcuts:
            // Alt+G / Ctrl+Shift+G : Summon or Toggle FERDOWSI Card
            // Alt+V / Ctrl+Shift+V : Start / Stop Voice Dictation (🎤)
            // Alt+R / Ctrl+Shift+R : Read Aloud / Stop Reading (🔊)
            // Alt+Q / Ctrl+Shift+F : Instant Quick-Fix (Fix ✨)
            NativeBridge.RegisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_ALT_G, NativeBridge.MOD_ALT, NativeBridge.VK_G);
            NativeBridge.RegisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_CTRL_SHIFT_G,
                NativeBridge.MOD_CONTROL | NativeBridge.MOD_SHIFT, NativeBridge.VK_G);
            NativeBridge.RegisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_ALT_V, NativeBridge.MOD_ALT, NativeBridge.VK_V);
            NativeBridge.RegisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_CTRL_SHIFT_V,
                NativeBridge.MOD_CONTROL | NativeBridge.MOD_SHIFT, NativeBridge.VK_V);
            NativeBridge.RegisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_ALT_R, NativeBridge.MOD_ALT, NativeBridge.VK_R);
            NativeBridge.RegisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_CTRL_SHIFT_R,
                NativeBridge.MOD_CONTROL | NativeBridge.MOD_SHIFT, NativeBridge.VK_R);
            NativeBridge.RegisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_ALT_Q, NativeBridge.MOD_ALT, NativeBridge.VK_Q);
            NativeBridge.RegisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_CTRL_SHIFT_F,
                NativeBridge.MOD_CONTROL | NativeBridge.MOD_SHIFT, NativeBridge.VK_F);

            PreviewKeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.Key == Key.Escape)
                {
                    if (_isVoiceRecording)
                    {
                        OnMicButtonClicked(_pillMicBtn, new RoutedEventArgs());
                        ke.Handled = true;
                    }
                    else if (_isReadingAloud)
                    {
                        StopReadAloudPlayback();
                        ke.Handled = true;
                    }
                    else if (_isExpanded)
                    {
                        SetExpanded(false);
                        ke.Handled = true;
                    }
                }
            };

            _aiPrefetchTimer = new DispatcherTimer();
            _aiPrefetchTimer.Interval = TimeSpan.FromMilliseconds(320);
            _aiPrefetchTimer.Tick += delegate
            {
                _aiPrefetchTimer.Stop();
                if (!string.IsNullOrEmpty(_activeSourceText) &&
                    _engine.Settings.AiProvider != "offline" &&
                    (_engine.HasAnyCloudAiKey || _engine.PhiSilicaState == 0 || _engine.Settings.AiProvider == "ai_only"))
                {
                    TriggerAnalysis(_activeSourceText, true, false);
                }
            };

            _pollTimer = new DispatcherTimer();
            _pollTimer.Interval = TimeSpan.FromMilliseconds(85);
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();
        }

        private void OnOverlayClosed(object sender, EventArgs e)
        {
            if (_pollTimer != null) _pollTimer.Stop();
            if (_aiPrefetchTimer != null) _aiPrefetchTimer.Stop();
            if (_hwnd != IntPtr.Zero)
            {
                NativeBridge.UnregisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_ALT_G);
                NativeBridge.UnregisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_CTRL_SHIFT_G);
                NativeBridge.UnregisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_ALT_V);
                NativeBridge.UnregisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_CTRL_SHIFT_V);
                NativeBridge.UnregisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_ALT_R);
                NativeBridge.UnregisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_CTRL_SHIFT_R);
                NativeBridge.UnregisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_ALT_Q);
                NativeBridge.UnregisterHotKey(_hwnd, NativeBridge.HOTKEY_ID_CTRL_SHIFT_F);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeBridge.WM_MOUSEACTIVATE)
            {
                if (!_allowInputBoxFocus)
                {
                    handled = true;
                    return new IntPtr(NativeBridge.MA_NOACTIVATE);
                }
            }
            if (msg == NativeBridge.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == NativeBridge.HOTKEY_ID_ALT_G || id == NativeBridge.HOTKEY_ID_CTRL_SHIFT_G)
                {
                    handled = true;
                    if (_isExpanded && Visibility == Visibility.Visible)
                    {
                        SetExpanded(false);
                    }
                    else
                    {
                        SummonAboveCursor(true);
                    }
                }
                else if (id == NativeBridge.HOTKEY_ID_ALT_V || id == NativeBridge.HOTKEY_ID_CTRL_SHIFT_V)
                {
                    handled = true;
                    SummonAboveCursor(false);
                    OnMicButtonClicked(_pillMicBtn, new RoutedEventArgs());
                }
                else if (id == NativeBridge.HOTKEY_ID_ALT_R || id == NativeBridge.HOTKEY_ID_CTRL_SHIFT_R)
                {
                    handled = true;
                    SummonAboveCursor(false);
                    OnReadAloudClicked(_pillReadAloudBtn, new RoutedEventArgs());
                }
                else if (id == NativeBridge.HOTKEY_ID_ALT_Q || id == NativeBridge.HOTKEY_ID_CTRL_SHIFT_F)
                {
                    handled = true;
                    SummonAboveCursor(false);
                    OnQuickFixClicked(_pillQuickFixBtn, new RoutedEventArgs());
                }
            }
            return IntPtr.Zero;
        }

        private void BuildUi()
        {
            Grid host = new Grid();
            host.Margin = new Thickness(8); // Room for drop shadow

            // 1. Collapsed Floating Pill (Directly above cursor)
            _pillRoot = new Border();
            _pillRoot.Background = BgDark;
            _pillRoot.BorderBrush = Emerald;
            _pillRoot.BorderThickness = new Thickness(1.2);
            _pillRoot.CornerRadius = new CornerRadius(16);
            _pillRoot.Padding = new Thickness(6, 4, 8, 4);
            _pillRoot.Cursor = Cursors.Hand;
            _pillRoot.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 12,
                ShadowDepth = 3,
                Opacity = 0.48
            };
            _pillRoot.MouseLeftButtonUp += OnPillClicked;

            StackPanel pillRow = new StackPanel();
            pillRow.Orientation = Orientation.Horizontal;
            pillRow.VerticalAlignment = VerticalAlignment.Center;

            _pillMicBtn = CreateSmallButton("🎤", BgChip, EmeraldSoft);
            _pillMicBtn.Margin = new Thickness(1, 0, 4, 0);
            _pillMicBtn.Padding = new Thickness(6, 2, 6, 2);
            _pillMicBtn.ToolTip = "Voice Dictation (Alt+V) — click to start/stop recording";
            _pillMicBtn.Click += OnMicButtonClicked;

            _pillReadAloudBtn = CreateSmallButton("🔊", BgChip, EmeraldSoft);
            _pillReadAloudBtn.Margin = new Thickness(0, 0, 5, 0);
            _pillReadAloudBtn.Padding = new Thickness(5, 2, 5, 2);
            _pillReadAloudBtn.ToolTip = "Read Aloud (Alt+R) — click to listen or stop";
            _pillReadAloudBtn.Click += OnReadAloudClicked;

            _pillBadgeBorder = new Border();
            _pillBadgeBorder.Width = 22;
            _pillBadgeBorder.Height = 22;
            _pillBadgeBorder.CornerRadius = new CornerRadius(11);
            _pillBadgeBorder.Background = Emerald;
            _pillBadgeBorder.VerticalAlignment = VerticalAlignment.Center;
            _pillBadgeBorder.Margin = new Thickness(0, 0, 3, 0);
            _pillBadgeBorder.ToolTip = "Click to open FERDOWSI Card (Alt+G)";

            _pillBadgeText = new TextBlock();
            _pillBadgeText.Text = "✓";
            _pillBadgeText.Foreground = Brushes.White;
            _pillBadgeText.FontWeight = FontWeights.Bold;
            _pillBadgeText.FontSize = 11.5;
            _pillBadgeText.HorizontalAlignment = HorizontalAlignment.Center;
            _pillBadgeText.VerticalAlignment = VerticalAlignment.Center;
            _pillBadgeBorder.Child = _pillBadgeText;

            _pillSummaryText = new TextBlock();
            _pillSummaryText.Text = string.Empty;
            _pillSummaryText.Foreground = TextPrimary;
            _pillSummaryText.FontSize = 11.5;
            _pillSummaryText.FontWeight = FontWeights.SemiBold;
            _pillSummaryText.VerticalAlignment = VerticalAlignment.Center;
            _pillSummaryText.Margin = new Thickness(4, 0, 5, 0);
            _pillSummaryText.MaxWidth = 200;
            _pillSummaryText.TextTrimming = TextTrimming.CharacterEllipsis;
            _pillSummaryText.Visibility = Visibility.Collapsed;

            _pillQuickFixBtn = CreateSmallButton("Fix ✨", Emerald, Brushes.White);
            _pillQuickFixBtn.Margin = new Thickness(2, 0, 4, 0);
            _pillQuickFixBtn.Padding = new Thickness(7, 2, 7, 2);
            _pillQuickFixBtn.ToolTip = "Instant Quick-Fix (Alt+Q)";
            _pillQuickFixBtn.Visibility = Visibility.Collapsed;
            _pillQuickFixBtn.Click += OnQuickFixClicked;

            Button pillCloseBtn = CreateSmallButton("×", Brushes.Transparent, TextMuted);
            pillCloseBtn.Padding = new Thickness(4, 0, 2, 0);
            pillCloseBtn.FontSize = 13;
            pillCloseBtn.ToolTip = "Close or pause FERDOWSI (10 min / 30 min / 1 hr)";
            pillCloseBtn.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                SetCloseMenuOpen(true);
            };
            _pillRoot.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                SetCloseMenuOpen(true);
            };

            // Short window left-to-right order: [🎤] [🔊] [Red Error Count / ✓] [Error Preview] [Fix ✨] [×]
            pillRow.Children.Add(_pillMicBtn);
            pillRow.Children.Add(_pillReadAloudBtn);
            pillRow.Children.Add(_pillBadgeBorder);
            pillRow.Children.Add(_pillSummaryText);
            pillRow.Children.Add(_pillQuickFixBtn);
            pillRow.Children.Add(pillCloseBtn);
            _pillRoot.Child = pillRow;

            // 2. Expanded Assistant Card (Above cursor)
            _cardRoot = new Border();
            _cardRoot.Width = 496;
            _cardRoot.Background = BgDark;
            _cardRoot.BorderBrush = BorderSubtle;
            _cardRoot.BorderThickness = new Thickness(1.2);
            _cardRoot.CornerRadius = new CornerRadius(14);
            _cardRoot.Padding = new Thickness(12);
            _cardRoot.Visibility = Visibility.Collapsed;
            _cardRoot.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 5,
                Opacity = 0.55
            };

            StackPanel cardStack = new StackPanel();

            // Header Row
            DockPanel header = new DockPanel();
            header.Margin = new Thickness(0, 0, 0, 8);

            StackPanel brandPanel = new StackPanel();
            brandPanel.Orientation = Orientation.Horizontal;
            Border miniLogo = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(7),
                Background = Emerald, Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            miniLogo.Child = new TextBlock
            {
                Text = "F", Foreground = Brushes.White, FontWeight = FontWeights.Bold,
                FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            brandPanel.Children.Add(miniLogo);
            brandPanel.Children.Add(new TextBlock
            {
                Text = "FERDOWSI v2", Foreground = TextPrimary, FontWeight = FontWeights.Bold,
                FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center
            });

            _cardSourceBadge = new TextBlock
            {
                Text = "· Cursor", Foreground = EmeraldSoft, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0)
            };
            brandPanel.Children.Add(_cardSourceBadge);
            DockPanel.SetDock(brandPanel, Dock.Left);

            StackPanel headerActions = new StackPanel();
            headerActions.Orientation = Orientation.Horizontal;
            headerActions.HorizontalAlignment = HorizontalAlignment.Right;

            _cardMicBtn = CreateSmallButton("🎤", BgChip, EmeraldSoft);
            _cardMicBtn.ToolTip = "Voice Dictation (Alt+V) — click to start/stop recording";
            _cardMicBtn.Margin = new Thickness(0, 0, 4, 0);
            _cardMicBtn.Click += OnMicButtonClicked;

            _cardReadAloudBtn = CreateSmallButton("🔊 Read", BgChip, EmeraldSoft);
            _cardReadAloudBtn.ToolTip = "Read Aloud (Alt+R)";
            _cardReadAloudBtn.Margin = new Thickness(0, 0, 4, 0);
            _cardReadAloudBtn.Click += OnReadAloudClicked;

            Button grabBtn = CreateSmallButton("↻ Box", BgChip, TextPrimary);
            grabBtn.ToolTip = "Capture the entire sentence/box or current selection from target window";
            grabBtn.Margin = new Thickness(0, 0, 4, 0);
            grabBtn.Click += delegate { GrabFreshTextFromTarget(true, true); };

            _aiModeToggleBtn = CreateSmallButton("AI: Auto", EmeraldDark, EmeraldSoft);
            _aiModeToggleBtn.ToolTip = "Toggle Cloud AI / AI Only / Offline mode (configure API keys in ⚙ Settings)";
            _aiModeToggleBtn.Margin = new Thickness(0, 0, 4, 0);
            _aiModeToggleBtn.Click += delegate
            {
                if (_engine.Settings.AiProvider == "offline")
                {
                    _engine.Settings.AiProvider = _engine.HasAnyCloudAiKey ? "ai_only" : "auto";
                    _engine.UseLocalAi = true;
                }
                else if (_engine.Settings.AiProvider != "ai_only" && _engine.HasAnyCloudAiKey)
                {
                    _engine.Settings.AiProvider = "ai_only";
                    _engine.UseLocalAi = true;
                }
                else
                {
                    _engine.UseLocalAi = !_engine.UseLocalAi;
                }
                _engine.ApplyFullAiConfiguration();
                _engine.Settings.Save();
                UpdateAiButtonAppearance();
                if (!string.IsNullOrEmpty(_activeSourceText))
                {
                    TriggerAnalysis(_activeSourceText, true, false);
                }
            };

            Button helpBtn = CreateSmallButton("?", BgChip, EmeraldSoft);
            helpBtn.ToolTip = "Open Help Guide (PDF) / راهنمای فارسی و انگلیسی";
            helpBtn.Margin = new Thickness(0, 0, 4, 0);
            helpBtn.Click += delegate
            {
                AppIconGenerator.OpenHelpPdf();
            };

            Button studioBtn = CreateSmallButton("⚙", BgChip, TextPrimary);
            studioBtn.ToolTip = "Open FERDOWSI Settings";
            studioBtn.Margin = new Thickness(0, 0, 4, 0);
            studioBtn.Click += delegate
            {
                if (_openStudioCallback != null) _openStudioCallback();
            };

            Button collapseBtn = CreateSmallButton("—", BgChip, TextPrimary);
            collapseBtn.ToolTip = "Collapse back to cursor pill (Esc)";
            collapseBtn.Click += delegate { SetExpanded(false); };

            headerActions.Children.Add(_cardMicBtn);
            headerActions.Children.Add(_cardReadAloudBtn);
            headerActions.Children.Add(grabBtn);
            headerActions.Children.Add(_aiModeToggleBtn);
            headerActions.Children.Add(helpBtn);
            headerActions.Children.Add(studioBtn);
            headerActions.Children.Add(collapseBtn);
            DockPanel.SetDock(headerActions, Dock.Right);

            header.Children.Add(headerActions);
            header.Children.Add(brandPanel);
            cardStack.Children.Add(header);

            // Mode Tabs Row: Correct | Reword | Format | Dictionary
            System.Windows.Controls.Primitives.UniformGrid tabBar = UniformGridRows1(4);
            tabBar.Margin = new Thickness(0, 0, 0, 8);
            _tabCorrectBtn = CreateTabButton("✓ Correct", true);
            _tabRewordBtn = CreateTabButton("✨ Reword", false);
            _tabFormatBtn = CreateTabButton("Aa Format", false);
            _tabDictBtn = CreateTabButton("📖 Dictionary", false);

            _tabCorrectBtn.Click += delegate { SwitchTab("correct"); };
            _tabRewordBtn.Click += delegate { SwitchTab("reword"); };
            _tabFormatBtn.Click += delegate { SwitchTab("format"); };
            _tabDictBtn.Click += delegate { SwitchTab("dictionary"); };

            tabBar.Children.Add(_tabCorrectBtn);
            tabBar.Children.Add(_tabRewordBtn);
            tabBar.Children.Add(_tabFormatBtn);
            tabBar.Children.Add(_tabDictBtn);
            cardStack.Children.Add(tabBar);

            // Dynamic Tab Sub-options Host
            _tabContentHost = new StackPanel();

            // Reword Section: Preset Tone Chips + Custom Instruction Input Box
            _rewordSectionPanel = new StackPanel();
            _rewordSectionPanel.Margin = new Thickness(0, 0, 0, 6);

            _rewordChipsWrap = new WrapPanel();
            _rewordChipsWrap.Margin = new Thickness(0, 0, 0, 5);
            string[] tones = new string[] { "Fluent", "Formal", "Casual", "Concise", "Expand", "Persuasive" };
            for (int i = 0; i < tones.Length; i++)
            {
                string toneKey = tones[i].ToLowerInvariant();
                Button chip = CreateChipButton(tones[i], toneKey == _activeRewordMode);
                chip.Tag = toneKey;
                chip.Click += OnRewordChipClicked;
                _rewordChipsWrap.Children.Add(chip);
            }
            Button altVarBtn = CreateChipButton("↻ Alt", false);
            altVarBtn.ToolTip = "Cycle alternative phrasing";
            altVarBtn.Click += delegate
            {
                _rewordVariantIndex++;
                RunRewordTransform();
            };
            _rewordChipsWrap.Children.Add(altVarBtn);
            _rewordSectionPanel.Children.Add(_rewordChipsWrap);

            // Custom Format / Reword Instruction Box
            DockPanel customRewordRow = new DockPanel();
            customRewordRow.Margin = new Thickness(0, 0, 0, 2);

            _customRewordApplyBtn = CreateSmallButton("Apply ✨", Emerald, Brushes.White);
            _customRewordApplyBtn.Padding = new Thickness(10, 5, 10, 5);
            _customRewordApplyBtn.Margin = new Thickness(6, 0, 0, 0);
            _customRewordApplyBtn.ToolTip = "Rewrite or format using your custom instruction";
            _customRewordApplyBtn.Click += delegate
            {
                RunCustomRewordTransform();
            };
            DockPanel.SetDock(_customRewordApplyBtn, Dock.Right);
            customRewordRow.Children.Add(_customRewordApplyBtn);

            _customRewordInputBox = new TextBox
            {
                Background = BgSurface,
                Foreground = TextMuted,
                CaretBrush = EmeraldSoft,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 11.5,
                Text = "Write custom format/style (e.g. formal email, 3 bullets, polite Persian)...",
                ToolTip = "Type any custom format or style instruction and press Enter or click Apply ✨"
            };
            _customRewordInputBox.PreviewMouseLeftButtonDown += delegate
            {
                _allowInputBoxFocus = true;
                NativeBridge.SetWindowActivable(_hwnd, true);
                try { Activate(); } catch { }
                if (_customRewordInputBox.Foreground == TextMuted)
                {
                    _customRewordInputBox.Text = string.Empty;
                    _customRewordInputBox.Foreground = TextPrimary;
                }
                _customRewordInputBox.Focus();
            };
            _customRewordInputBox.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.Key == Key.Enter)
                {
                    ke.Handled = true;
                    RunCustomRewordTransform();
                }
            };
            customRewordRow.Children.Add(_customRewordInputBox);
            _rewordSectionPanel.Children.Add(customRewordRow);

            // Format Mode Chips
            _formatChipsWrap = new WrapPanel();
            _formatChipsWrap.Margin = new Thickness(0, 0, 0, 6);
            string[,] formats = new string[,]
            {
                { "Finglish → فارسی", "finglish_to_fa" },
                { "نیم‌فاصله فارسی", "persian_zwnj" },
                { "Sentence case", "sentence_case" },
                { "Title Case", "title_case" },
                { "UPPERCASE", "upper_case" },
                { "lowercase", "lower_case" },
                { "• Bullet List", "bullet_list" },
                { "1. Numbered", "numbered_list" },
                { "✉ Email", "email_format" },
                { "Clean Spaces", "clean_text" },
                { "snake_case", "snake_case" },
                { "camelCase", "camel_case" }
            };
            for (int i = 0; i < formats.GetLength(0); i++)
            {
                string label = formats[i, 0];
                string key = formats[i, 1];
                Button chip = CreateChipButton(label, key == _activeFormatMode);
                chip.Tag = key;
                chip.Click += OnFormatChipClicked;
                _formatChipsWrap.Children.Add(chip);
            }

            // Diff Preview Block (for Correct mode)
            Border diffBox = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 0, 6),
                MaxHeight = 115
            };
            ScrollViewer diffScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _diffPreviewBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = TextPrimary,
                LineHeight = 18
            };
            diffScroll.Content = _diffPreviewBlock;
            diffBox.Child = diffScroll;

            _issuesWrap = new WrapPanel();
            _issuesWrap.Margin = new Thickness(0, 0, 0, 6);

            // Output Preview Box (for Reword & Format modes, or editable preview)
            _outputPreviewBox = new TextBox
            {
                Background = BgSurface,
                Foreground = TextPrimary,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 64,
                MaxHeight = 125,
                IsReadOnly = true,
                Margin = new Thickness(0, 0, 0, 8)
            };

            _dictBox = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                MaxHeight = 258,
                Visibility = Visibility.Collapsed
            };
            ScrollViewer dictScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _dictContentStack = new StackPanel();
            dictScroll.Content = _dictContentStack;
            _dictBox.Child = dictScroll;

            _tabContentHost.Children.Add(_rewordSectionPanel);
            _tabContentHost.Children.Add(_formatChipsWrap);
            _tabContentHost.Children.Add(diffBox);
            _tabContentHost.Children.Add(_issuesWrap);
            _tabContentHost.Children.Add(_outputPreviewBox);
            _tabContentHost.Children.Add(_dictBox);
            cardStack.Children.Add(_tabContentHost);

            // Footer Action Bar + Latency Meter
            DockPanel footer = new DockPanel();
            _cardTimingBadge = new TextBlock
            {
                Text = "Ready",
                Foreground = TextMuted,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_cardTimingBadge, Dock.Left);

            StackPanel actionBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _copyBtn = CreateSmallButton("Copy", BgChip, TextPrimary);
            _copyBtn.Padding = new Thickness(11, 5, 11, 5);
            _copyBtn.Margin = new Thickness(0, 0, 6, 0);
            _copyBtn.Click += OnCopyClicked;

            _replaceBtn = CreateSmallButton("✨ Replace at Cursor", Emerald, Brushes.White);
            _replaceBtn.Padding = new Thickness(13, 5, 13, 5);
            _replaceBtn.FontWeight = FontWeights.Bold;
            _replaceBtn.Click += OnReplaceClicked;

            actionBtns.Children.Add(_copyBtn);
            actionBtns.Children.Add(_replaceBtn);
            DockPanel.SetDock(actionBtns, Dock.Right);

            footer.Children.Add(actionBtns);
            footer.Children.Add(_cardTimingBadge);
            cardStack.Children.Add(footer);

            _cardRoot.Child = cardStack;

            // 3. Custom Dark Close / Pause Menu Card (Replaces broken white-gutter Win32 ContextMenu)
            _closeMenuRoot = BuildCustomCloseMenuCard();

            host.Children.Add(_pillRoot);
            host.Children.Add(_cardRoot);
            host.Children.Add(_closeMenuRoot);
            Content = host;

            UpdateAiButtonAppearance();
            UpdateVoiceModeAppearance();
            UpdateShortWindowButtonsVisibility();
            UpdateTabVisibility();
        }

        private static System.Windows.Controls.Primitives.UniformGrid UniformGridRows1(int cols)
        {
            System.Windows.Controls.Primitives.UniformGrid ug = new System.Windows.Controls.Primitives.UniformGrid();
            ug.Rows = 1;
            ug.Columns = cols;
            return ug;
        }

        private Button CreateSmallButton(string text, Brush bg, Brush fg)
        {
            Button btn = new Button();
            btn.Content = text;
            btn.Background = bg;
            btn.Foreground = fg;
            btn.BorderThickness = new Thickness(0);
            btn.Padding = new Thickness(8, 3, 8, 3);
            btn.FontSize = 11.5;
            btn.FontWeight = FontWeights.SemiBold;
            btn.Cursor = Cursors.Hand;
            btn.Focusable = false;

            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            btn.Template = tpl;
            return btn;
        }

        private Button CreateTabButton(string text, bool active)
        {
            Button btn = CreateSmallButton(text, active ? Emerald : BgSurface, active ? Brushes.White : TextMuted);
            btn.Margin = new Thickness(2, 0, 2, 0);
            btn.Padding = new Thickness(8, 5, 8, 5);
            return btn;
        }

        private Button CreateChipButton(string text, bool active)
        {
            Button btn = CreateSmallButton(text, active ? Emerald : BgChip, active ? Brushes.White : TextPrimary);
            btn.Margin = new Thickness(0, 0, 5, 5);
            btn.Padding = new Thickness(8, 3, 8, 3);
            btn.FontSize = 11;
            return btn;
        }

        private Button CreateMenuActionButton(string text, Brush bg, Brush fg)
        {
            Button btn = new Button();
            btn.Content = text;
            btn.Background = bg;
            btn.Foreground = fg;
            btn.BorderThickness = new Thickness(0);
            btn.Padding = new Thickness(10, 6, 10, 6);
            btn.Margin = new Thickness(0, 2, 0, 2);
            btn.FontSize = 12;
            btn.FontWeight = FontWeights.SemiBold;
            btn.Cursor = Cursors.Hand;
            btn.Focusable = false;
            btn.HorizontalAlignment = HorizontalAlignment.Stretch;

            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            btn.Template = tpl;
            return btn;
        }

        private Border BuildCustomCloseMenuCard()
        {
            Border card = new Border();
            card.Width = 268;
            card.Background = BgDark;
            card.BorderBrush = Emerald;
            card.BorderThickness = new Thickness(1.2);
            card.CornerRadius = new CornerRadius(12);
            card.Padding = new Thickness(10, 8, 10, 8);
            card.Visibility = Visibility.Collapsed;
            card.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 4,
                Opacity = 0.55
            };

            StackPanel stack = new StackPanel();

            DockPanel topRow = new DockPanel();
            topRow.Margin = new Thickness(2, 0, 0, 6);

            Button backBtn = CreateSmallButton("← Back", BgChip, TextPrimary);
            backBtn.Padding = new Thickness(8, 2, 8, 2);
            backBtn.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                SetCloseMenuOpen(false);
            };
            DockPanel.SetDock(backBtn, Dock.Right);
            topRow.Children.Add(backBtn);

            TextBlock titleTxt = new TextBlock
            {
                Text = "⏸ Pause or Close FERDOWSI",
                Foreground = EmeraldSoft,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            topRow.Children.Add(titleTxt);
            stack.Children.Add(topRow);

            Button bDismiss = CreateMenuActionButton("✕   Dismiss for current text box", BgSurface, TextPrimary);
            bDismiss.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                _isCloseMenuOpen = false;
                _dismissedForCurrentFocus = true;
                HideOverlay();
            };

            Button b10 = CreateMenuActionButton("⏸   Close for 10 minutes (10 min)", BgSurface, EmeraldSoft);
            b10.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                _isCloseMenuOpen = false;
                SnoozeForMinutes(10);
            };

            Button b30 = CreateMenuActionButton("⏸   Close for 30 minutes (30 min)", BgSurface, EmeraldSoft);
            b30.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                _isCloseMenuOpen = false;
                SnoozeForMinutes(30);
            };

            Button b60 = CreateMenuActionButton("⏸   Close for 1 hour (1 hr)", BgSurface, EmeraldSoft);
            b60.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                _isCloseMenuOpen = false;
                SnoozeForMinutes(60);
            };

            Button bOff = CreateMenuActionButton("🚫   Turn off floating widget", CoralDark, CoralSoft);
            bOff.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                _isCloseMenuOpen = false;
                FloatingEnabled = false;
                _engine.Settings.FloatingEnabled = false;
                _engine.Settings.Save();
                HideOverlay();
                if (SnoozeStateChanged != null) SnoozeStateChanged();
            };

            stack.Children.Add(bDismiss);
            stack.Children.Add(b10);
            stack.Children.Add(b30);
            stack.Children.Add(b60);
            stack.Children.Add(bOff);

            card.Child = stack;
            return card;
        }

        private void SetCloseMenuOpen(bool open)
        {
            _isCloseMenuOpen = open;
            _lastActivityUtc = DateTime.UtcNow;
            if (open)
            {
                _isExpanded = false;
                _pillRoot.Visibility = Visibility.Collapsed;
                _cardRoot.Visibility = Visibility.Collapsed;
                _closeMenuRoot.Visibility = Visibility.Visible;
            }
            else
            {
                _closeMenuRoot.Visibility = Visibility.Collapsed;
                _pillRoot.Visibility = _isExpanded ? Visibility.Collapsed : Visibility.Visible;
                _cardRoot.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateLayout();
            PositionAbovePhysicalPoint(_anchorCaretX, _anchorCaretY, _anchorCaretH, true);
        }

        private void UpdateVoiceModeAppearance()
        {
            // Voice Output Mode and Voice Language are now configured in Main Settings.
        }

        public void UpdateShortWindowButtonsVisibility()
        {
            if (_pillMicBtn != null)
            {
                _pillMicBtn.Visibility = _engine.Settings.ShowMicOnShortWindow ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_pillReadAloudBtn != null)
            {
                _pillReadAloudBtn.Visibility = _engine.Settings.ShowReadAloudOnShortWindow ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void OnReadAloudClicked(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _lastActivityUtc = DateTime.UtcNow;

            if (_isReadingAloud)
            {
                StopReadAloudPlayback();
                _pillSummaryText.Visibility = Visibility.Collapsed;
                _cardTimingBadge.Text = "⏹ Stopped Read Aloud playback";
                return;
            }

            string textToRead = !string.IsNullOrEmpty(_currentOutputText) && _isExpanded
                ? _currentOutputText
                : _activeSourceText;

            if (string.IsNullOrEmpty(textToRead) || textToRead.Trim().Length == 0)
            {
                CaptureTargetTextCore(true, true);
                textToRead = _activeSourceText;
            }

            if (string.IsNullOrEmpty(textToRead) || textToRead.Trim().Length == 0)
            {
                _pillSummaryText.Text = "Select text first";
                _pillSummaryText.Visibility = Visibility.Visible;
                _cardTimingBadge.Text = "🔊 Select or type text first to read aloud";
                return;
            }

            _isReadingAloud = true;
            _pillReadAloudBtn.Content = "⏳";
            _pillReadAloudBtn.Background = EmeraldDark;
            _cardReadAloudBtn.Content = "⏳...";
            _cardReadAloudBtn.Background = EmeraldDark;
            _pillSummaryText.Text = "Synthesizing...";
            _pillSummaryText.Visibility = Visibility.Visible;
            _cardTimingBadge.Text = "🔊 Synthesizing speech audio...";

            string ttsWav = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ferdowsi_tts_" + Process.GetCurrentProcess().Id + ".wav");

            try
            {
                ReadAloudResult rr = await _engine.ReadAloudAsync(textToRead, ttsWav);
                if (!_isReadingAloud) return;

                if (rr != null && rr.Ok && !string.IsNullOrEmpty(rr.WavPath) && System.IO.File.Exists(rr.WavPath))
                {
                    _pillReadAloudBtn.Content = "⏹";
                    _pillReadAloudBtn.Background = Coral;
                    _pillReadAloudBtn.Foreground = Brushes.White;
                    _cardReadAloudBtn.Content = "⏹ Stop";
                    _cardReadAloudBtn.Background = Coral;
                    _cardReadAloudBtn.Foreground = Brushes.White;
                    _pillSummaryText.Text = "🔊 Reading aloud...";
                    _cardTimingBadge.Text = string.Format("🔊 {0} · {1:0.0} ms", rr.Engine, rr.ElapsedMs);

                    string playPath = rr.WavPath;
                    await System.Threading.Tasks.Task.Run(delegate
                    {
                        try
                        {
                            using (System.Media.SoundPlayer sp = new System.Media.SoundPlayer(playPath))
                            {
                                _ttsPlayer = sp;
                                sp.PlaySync();
                            }
                        }
                        catch { }
                        finally
                        {
                            _ttsPlayer = null;
                        }
                    });
                }
                else
                {
                    string err = (rr != null && !string.IsNullOrEmpty(rr.Error)) ? rr.Error : "Could not synthesize speech";
                    _pillSummaryText.Text = "🔊 " + err;
                    _cardTimingBadge.Text = "Read Aloud: " + err;
                }
            }
            finally
            {
                StopReadAloudPlayback();
            }
        }

        private void StopReadAloudPlayback()
        {
            _isReadingAloud = false;
            try
            {
                System.Media.SoundPlayer sp = _ttsPlayer;
                if (sp != null) sp.Stop();
            }
            catch { }
            if (_pillReadAloudBtn != null)
            {
                _pillReadAloudBtn.Content = "🔊";
                _pillReadAloudBtn.Background = BgChip;
                _pillReadAloudBtn.Foreground = EmeraldSoft;
            }
            if (_cardReadAloudBtn != null)
            {
                _cardReadAloudBtn.Content = "🔊 Read";
                _cardReadAloudBtn.Background = BgChip;
                _cardReadAloudBtn.Foreground = EmeraldSoft;
            }
        }

        private async void OnMicButtonClicked(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _lastActivityUtc = DateTime.UtcNow;
            if (_voiceBusy || !NativeBridge.IsNativeLoaded) return;

            if (!_isVoiceRecording)
            {
                if (!_engine.HasAnySttKey)
                {
                    _pillSummaryText.Text = "Set Groq or Deepgram Key in ⚙";
                    _cardTimingBadge.Text = "Add Groq or Deepgram API key in Studio Settings ⚙";
                    if (_openStudioCallback != null) _openStudioCallback();
                    return;
                }

                if (_activeTargetHwnd == 0)
                {
                    GvMonitorState st;
                    if (NativeBridge.gv_poll_state(out st) != 0 && st.foreground_hwnd != 0)
                    {
                        _activeTargetHwnd = st.foreground_hwnd;
                    }
                }

                int started = NativeBridge.gv_voice_start_recording();
                if (started == 1)
                {
                    _isVoiceRecording = true;
                    _pillMicBtn.Content = "🔴";
                    _pillMicBtn.Background = Coral;
                    _pillMicBtn.Foreground = Brushes.White;
                    _cardMicBtn.Content = "🔴 Stop";
                    _cardMicBtn.Background = Coral;
                    _cardMicBtn.Foreground = Brushes.White;
                    string modeTag = string.Equals(_engine.Settings.VoiceMode, "exact", StringComparison.OrdinalIgnoreCase)
                        ? "Exact Voice"
                        : "Cleanup Voice";
                    _pillSummaryText.Text = "Listening...";
                    _pillSummaryText.Visibility = Visibility.Visible;
                    _cardTimingBadge.Text = "🎙 Listening (" + modeTag + ")... speak now, then click 🔴 Stop";
                }
                else
                {
                    _pillSummaryText.Text = "Mic error";
                    _pillSummaryText.Visibility = Visibility.Visible;
                    _cardTimingBadge.Text = "Could not open microphone input";
                }
            }
            else
            {
                _isVoiceRecording = false;
                _voiceBusy = true;
                _pillMicBtn.Content = "⏳";
                _pillMicBtn.Background = BgChip;
                _pillMicBtn.Foreground = EmeraldSoft;
                _cardMicBtn.Content = "⏳...";
                _cardMicBtn.Background = BgChip;
                _cardMicBtn.Foreground = EmeraldSoft;
                _pillSummaryText.Text = "Transcribing...";
                _pillSummaryText.Visibility = Visibility.Visible;
                _cardTimingBadge.Text = "⏳ Transcribing & processing voice...";

                string wavPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "ferdowsi_voice_" + Process.GetCurrentProcess().Id + ".wav");
                try
                {
                    int recordedBytes = NativeBridge.gv_voice_stop_recording(wavPath);
                    if (recordedBytes <= 0)
                    {
                        _pillSummaryText.Text = "No voice audio captured";
                        _cardTimingBadge.Text = "No voice audio captured";
                    }
                    else
                    {
                        VoiceTranscribeResult vr = await _engine.TranscribeAudioAsync(wavPath);
                        try
                        {
                            if (System.IO.File.Exists(wavPath)) System.IO.File.Delete(wavPath);
                        }
                        catch { }

                        if (vr != null && vr.Ok && !string.IsNullOrEmpty(vr.Transcript))
                        {
                            long targetHwnd = _activeTargetHwnd;
                            if (targetHwnd == 0)
                            {
                                GvMonitorState st;
                                if (NativeBridge.gv_poll_state(out st) != 0)
                                {
                                    targetHwnd = st.foreground_hwnd;
                                }
                            }
                            string dictated = vr.Transcript.Trim();
                            NativeBridge.gv_insert_text_at_cursor(targetHwnd, dictated + " ");
                            _activeSourceText = dictated;
                            _currentOutputText = dictated;
                            _replaceMode = 1;
                            _typedBackspaceCount = dictated.Length + 1;
                            _sourceLabel = "Voice Dictation";
                            _pillSummaryText.Text = "🎤 " + dictated;
                            _cardTimingBadge.Text = string.Format("✓ {0} · {1:0.0} ms", vr.Engine, vr.ElapsedMs);
                            TriggerAnalysis(dictated, true, false);
                        }
                        else
                        {
                            string err = (vr != null && !string.IsNullOrEmpty(vr.Error)) ? vr.Error : "No speech detected";
                            _pillSummaryText.Text = "🎤 " + err;
                            _cardTimingBadge.Text = "Voice: " + err;
                        }
                    }
                }
                finally
                {
                    _voiceBusy = false;
                    _pillMicBtn.Content = "🎤";
                    _pillMicBtn.Background = BgChip;
                    _pillMicBtn.Foreground = EmeraldSoft;
                    _cardMicBtn.Content = "🎤";
                    _cardMicBtn.Background = BgChip;
                    _cardMicBtn.Foreground = EmeraldSoft;
                }
            }
        }

        private void UpdateAiButtonAppearance()
        {
            if (_engine.UseLocalAi && _engine.Settings.AiProvider != "offline")
            {
                string label;
                string prov = _engine.Settings.AiProvider ?? "auto";
                if (prov == "ai_only")
                {
                    label = "AI Only ✨";
                }
                else if (prov == "deepseek" && _engine.HasDeepSeekKey)
                {
                    label = "DeepSeek: ON ✨";
                }
                else if (_engine.HasGoogleKey && (prov == "auto" || prov == "google"))
                {
                    label = "Gemini: ON ✨";
                }
                else if (_engine.HasDeepSeekKey && prov == "auto")
                {
                    label = "DeepSeek: ON ✨";
                }
                else if (_engine.PhiSilicaState == 0)
                {
                    label = "Phi Silica: ON";
                }
                else
                {
                    label = "AI: Auto";
                }
                _aiModeToggleBtn.Content = label;
                _aiModeToggleBtn.Background = EmeraldDark;
                _aiModeToggleBtn.Foreground = EmeraldSoft;
            }
            else
            {
                _aiModeToggleBtn.Content = "AI: Offline";
                _aiModeToggleBtn.Background = BgChip;
                _aiModeToggleBtn.Foreground = TextMuted;
            }
        }

        private void OnPillClicked(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;
            if (_replaceMode == 0 && !string.IsNullOrEmpty(_activeSourceText))
            {
                TriggerAnalysis(_activeSourceText, true, false);
            }
            else
            {
                GrabFreshTextFromTarget(false, true);
            }
            SetExpanded(true);
        }

        private async void OnQuickFixClicked(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _pillQuickFixBtn.IsEnabled = false;
            _pillQuickFixBtn.Content = "...";
            try
            {
                if (_replaceMode != 0)
                {
                    CaptureTargetTextCore(false, true);
                }

                if (!string.IsNullOrEmpty(_activeSourceText) && _activeSourceText.Trim().Length > 0)
                {
                    AnalyzeResponse fullRes = await _engine.AnalyzeAsync(_activeSourceText, false);
                    if (fullRes != null && !string.IsNullOrEmpty(fullRes.Corrected))
                    {
                        _latestAnalysis = fullRes;
                        _lastAnalyzedText = _activeSourceText;
                        _lastAnalysisWasFast = false;
                        _currentOutputText = fullRes.Corrected;
                        UpdatePillAppearance(fullRes);
                        if (_currentOutputText != _activeSourceText)
                        {
                            ApplyReplacementToTarget();
                        }
                    }
                }
            }
            finally
            {
                _pillQuickFixBtn.Content = "Fix ✨";
                _pillQuickFixBtn.IsEnabled = true;
            }
        }

        public void SummonAboveCursor(bool forceExpand)
        {
            _dismissedForCurrentFocus = false;
            _snoozeUntilUtc = DateTime.MinValue;
            _lastActivityUtc = DateTime.UtcNow;

            if (NativeBridge.IsNativeLoaded)
            {
                GvMonitorState st;
                NativeBridge.gv_poll_state(out st);
                if (st.foreground_hwnd != 0) _activeTargetHwnd = st.foreground_hwnd;
                ResolveBestCaretAnchor(st, NativeBridge.InspectFocusedUiaElement(_hwnd), true);
            }

            GrabFreshTextFromTarget(true, true);
            if (forceExpand) SetExpanded(true);
            EnsureVisibleAtAnchor();
        }

        private void SetExpanded(bool expanded)
        {
            _isExpanded = expanded;
            _isCloseMenuOpen = false;
            _lastActivityUtc = DateTime.UtcNow;
            if (!expanded && _allowInputBoxFocus)
            {
                _allowInputBoxFocus = false;
                NativeBridge.SetWindowActivable(_hwnd, false);
                if (_activeTargetHwnd != 0)
                {
                    try { NativeBridge.SetForegroundWindow(new IntPtr(_activeTargetHwnd)); } catch { }
                }
            }
            if (_closeMenuRoot != null) _closeMenuRoot.Visibility = Visibility.Collapsed;
            _pillRoot.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            _cardRoot.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

            if (expanded)
            {
                if (!string.IsNullOrEmpty(_activeSourceText) &&
                    (_lastAnalysisWasFast ||
                     (_engine.Settings.AiProvider != "offline" &&
                      (_engine.HasAnyCloudAiKey || _engine.Settings.AiProvider == "ai_only") &&
                      (_latestAnalysis == null || string.IsNullOrEmpty(_latestAnalysis.Engine) || _latestAnalysis.Engine.StartsWith("Python")))))
                {
                    TriggerAnalysis(_activeSourceText, true, false);
                }
                RefreshActiveTabOutput();
            }

            UpdateLayout();
            PositionAbovePhysicalPoint(_anchorCaretX, _anchorCaretY, _anchorCaretH, true);
        }

        private void HideOverlay()
        {
            if (_allowInputBoxFocus)
            {
                _allowInputBoxFocus = false;
                NativeBridge.SetWindowActivable(_hwnd, false);
            }
            _isExpanded = false;
            _isCloseMenuOpen = false;
            if (_closeMenuRoot != null) _closeMenuRoot.Visibility = Visibility.Collapsed;
            _pillRoot.Visibility = Visibility.Visible;
            _cardRoot.Visibility = Visibility.Collapsed;
            if (Visibility == Visibility.Visible)
            {
                Visibility = Visibility.Hidden;
            }
        }

        private void SwitchTab(string tab)
        {
            _activeTab = tab;
            _tabCorrectBtn.Background = (tab == "correct") ? Emerald : BgSurface;
            _tabCorrectBtn.Foreground = (tab == "correct") ? Brushes.White : TextMuted;

            _tabRewordBtn.Background = (tab == "reword") ? Emerald : BgSurface;
            _tabRewordBtn.Foreground = (tab == "reword") ? Brushes.White : TextMuted;

            _tabFormatBtn.Background = (tab == "format") ? Emerald : BgSurface;
            _tabFormatBtn.Foreground = (tab == "format") ? Brushes.White : TextMuted;

            _tabDictBtn.Background = (tab == "dictionary") ? Emerald : BgSurface;
            _tabDictBtn.Foreground = (tab == "dictionary") ? Brushes.White : TextMuted;

            UpdateTabVisibility();
            RefreshActiveTabOutput();
            UpdateLayout();
            PositionAbovePhysicalPoint(_anchorCaretX, _anchorCaretY, _anchorCaretH, true);
        }

        private void UpdateTabVisibility()
        {
            bool isCorrect = (_activeTab == "correct");
            bool isReword = (_activeTab == "reword");
            bool isFormat = (_activeTab == "format");
            bool isDict = (_activeTab == "dictionary");

            _rewordSectionPanel.Visibility = isReword ? Visibility.Visible : Visibility.Collapsed;
            _formatChipsWrap.Visibility = isFormat ? Visibility.Visible : Visibility.Collapsed;

            // child[2] is diffBox
            _tabContentHost.Children[2].Visibility = isCorrect ? Visibility.Visible : Visibility.Collapsed;
            _issuesWrap.Visibility = isCorrect ? Visibility.Visible : Visibility.Collapsed;
            _outputPreviewBox.Visibility = (isReword || isFormat) ? Visibility.Visible : Visibility.Collapsed;
            _dictBox.Visibility = isDict ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnRewordChipClicked(object sender, RoutedEventArgs e)
        {
            Button clicked = sender as Button;
            if (clicked == null || clicked.Tag == null) return;
            _activeRewordMode = clicked.Tag.ToString();
            _rewordVariantIndex = 0;

            foreach (UIElement el in _rewordChipsWrap.Children)
            {
                Button b = el as Button;
                if (b != null && b.Tag != null)
                {
                    bool act = (b.Tag.ToString() == _activeRewordMode);
                    b.Background = act ? Emerald : BgChip;
                    b.Foreground = act ? Brushes.White : TextPrimary;
                }
            }
            RunRewordTransform();
        }

        private void OnFormatChipClicked(object sender, RoutedEventArgs e)
        {
            Button clicked = sender as Button;
            if (clicked == null || clicked.Tag == null) return;
            _activeFormatMode = clicked.Tag.ToString();

            foreach (UIElement el in _formatChipsWrap.Children)
            {
                Button b = el as Button;
                if (b != null && b.Tag != null)
                {
                    bool act = (b.Tag.ToString() == _activeFormatMode);
                    b.Background = act ? Emerald : BgChip;
                    b.Foreground = act ? Brushes.White : TextPrimary;
                }
            }
            RunFormatTransform();
        }

        private bool CaptureTargetTextCore(bool probeSelectionCtrlC, bool grabFullBoxViaCtrlA)
        {
            UiaInspectionResult uia = NativeBridge.InspectFocusedUiaElement(_hwnd);
            _cachedUia = uia;
            if (uia != null && uia.IsPassword)
            {
                _activeSourceText = string.Empty;
                _lastAnalyzedText = null;
                NativeBridge.gv_clear_typed_buffer();
                return false;
            }
            _activeUiaElement = uia.Element;
            _activeSupportsValueSet = uia.SupportsValueSet;

            if (!string.IsNullOrEmpty(uia.SelectedText) && uia.SelectedText.Trim().Length > 0)
            {
                _activeSourceText = uia.SelectedText;
                _replaceMode = 0;
                _sourceLabel = "Selected Text";
                return true;
            }

            if (probeSelectionCtrlC && _activeTargetHwnd != 0)
            {
                string clipSel = NativeBridge.CaptureSelectionFromTarget(_activeTargetHwnd);
                if (!string.IsNullOrEmpty(clipSel) && clipSel.Trim().Length > 0)
                {
                    _activeSourceText = clipSel;
                    _replaceMode = 0;
                    _sourceLabel = "Selected Text";
                    return true;
                }
            }

            bool isHugeDoc = (!string.IsNullOrEmpty(uia.ControlValueText) && uia.ControlValueText.Length > 3500);
            if (grabFullBoxViaCtrlA && _activeTargetHwnd != 0 && !isHugeDoc)
            {
                string fullField = NativeBridge.CaptureFullFieldFromTarget(_activeTargetHwnd);
                if (!string.IsNullOrEmpty(fullField) && fullField.Trim().Length > 0)
                {
                    _activeSourceText = fullField;
                    _replaceMode = 3;
                    _sourceLabel = "Full Box";
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(uia.ControlValueText) && uia.ControlValueText.Trim().Length > 0 && !isHugeDoc)
            {
                _activeSourceText = uia.ControlValueText;
                _replaceMode = 3;
                _sourceLabel = "Full Box";
                return true;
            }

            string typed = NativeBridge.GetTypedBufferText();
            if (!string.IsNullOrEmpty(typed) && typed.Trim().Length > 0)
            {
                _activeSourceText = typed;
                _replaceMode = 1;
                _typedBackspaceCount = typed.Length;
                _sourceLabel = "Live Typing";
                return true;
            }

            return false;
        }

        private void GrabFreshTextFromTarget(bool probeSelectionCtrlC, bool grabFullBoxViaCtrlA)
        {
            if (CaptureTargetTextCore(probeSelectionCtrlC, grabFullBoxViaCtrlA))
            {
                TriggerAnalysis(_activeSourceText, true, false);
            }
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            if (!FloatingEnabled || IsSnoozed || !NativeBridge.IsNativeLoaded)
            {
                if (Visibility == Visibility.Visible) HideOverlay();
                return;
            }

            GvMonitorState st;
            if (NativeBridge.gv_poll_state(out st) == 0) return;

            bool focusChanged = false;
            if (st.foreground_hwnd != 0 && st.foreground_hwnd != _hwnd.ToInt64() && st.foreground_hwnd != _lastForegroundHwnd)
            {
                _lastForegroundHwnd = st.foreground_hwnd;
                _activeTargetHwnd = st.foreground_hwnd;
                _dismissedForCurrentFocus = false;
                focusChanged = true;
                if (_isExpanded) SetExpanded(false);
                if (_isCloseMenuOpen) SetCloseMenuOpen(false);
            }

            _pollTickCounter++;
            if (focusChanged || st.is_typing_active == 1 || _pollTickCounter % 2 == 0 || st.has_selection_event == 1)
            {
                _cachedUia = NativeBridge.InspectFocusedUiaElement(_hwnd);
            }

            // 0. PASSWORD PROTECTION: Immediately wipe buffer & hide overlay when any password field is focused!
            if (st.is_password_focused == 1 || (_cachedUia != null && _cachedUia.IsPassword))
            {
                _activeSourceText = string.Empty;
                _lastAnalyzedText = null;
                if (_aiPrefetchTimer != null) _aiPrefetchTimer.Stop();
                NativeBridge.gv_clear_typed_buffer();
                if (Visibility == Visibility.Visible) HideOverlay();
                return;
            }

            if (_isVoiceRecording || _voiceBusy)
            {
                _lastActivityUtc = DateTime.UtcNow;
                EnsureVisibleAtAnchor();
                return;
            }

            // 1. Handle fresh mouse/keyboard selection event immediately
            if (st.has_selection_event == 1)
            {
                NativeBridge.gv_ack_selection_event();
                _dismissedForCurrentFocus = false;
                _lastActivityUtc = DateTime.UtcNow;
                _activeTargetHwnd = st.foreground_hwnd;

                string selText = _cachedUia.SelectedText;
                if (string.IsNullOrEmpty(selText) && _activeTargetHwnd != 0)
                {
                    selText = NativeBridge.CaptureSelectionFromTarget(_activeTargetHwnd);
                }

                if (!string.IsNullOrEmpty(selText) && selText.Trim().Length > 0)
                {
                    _activeSourceText = selText;
                    _replaceMode = 0;
                    _sourceLabel = "Selected Text";
                    ResolveBestCaretAnchor(st, _cachedUia, true);
                    EnsureVisibleAtAnchor();
                    TriggerAnalysis(_activeSourceText, true, false);
                    return;
                }
            }

            // 2. Handle live typing buffer updates (prefer full box via passive UIA so leading words are never missed!)
            if (st.is_typing_active == 1 && st.typed_char_count > 0)
            {
                _dismissedForCurrentFocus = false;
                _lastActivityUtc = DateTime.UtcNow;
                _activeTargetHwnd = st.foreground_hwnd;

                string liveTyped = NativeBridge.GetTypedBufferText();
                if (!string.IsNullOrEmpty(liveTyped))
                {
                    string candidateText = liveTyped;
                    string trimmedTyped = liveTyped.Trim();
                    bool uiaHasFullSentence = _cachedUia != null &&
                                              _cachedUia.IsEditable &&
                                              !string.IsNullOrEmpty(_cachedUia.ControlValueText) &&
                                              _cachedUia.ControlValueText.Length >= liveTyped.Length &&
                                              _cachedUia.ControlValueText.Length <= 2500 &&
                                              (_cachedUia.SupportsValueSet ||
                                               (trimmedTyped.Length > 0 &&
                                                _cachedUia.ControlValueText.IndexOf(trimmedTyped, StringComparison.Ordinal) >= 0));

                    if (uiaHasFullSentence)
                    {
                        candidateText = _cachedUia.ControlValueText;
                        _activeSourceText = candidateText;
                        _replaceMode = 3;
                        _typedBackspaceCount = candidateText.Length;
                        _sourceLabel = "Full Box";
                    }
                    else
                    {
                        _activeSourceText = liveTyped;
                        _replaceMode = 1;
                        _typedBackspaceCount = liveTyped.Length;
                        _sourceLabel = "Live Typing";
                    }

                    if (candidateText != _lastAnalyzedText && st.last_type_tick != _lastAnalyzedTypeTick)
                    {
                        _lastAnalyzedTypeTick = st.last_type_tick;
                        bool isAiOnly = (_engine.Settings.AiProvider == "ai_only");
                        if (isAiOnly)
                        {
                            if (_aiPrefetchTimer != null)
                            {
                                _aiPrefetchTimer.Stop();
                                _aiPrefetchTimer.Start();
                            }
                        }
                        else
                        {
                            TriggerAnalysis(candidateText, false, true);
                            if (_aiPrefetchTimer != null &&
                                _engine.Settings.AiProvider != "offline" &&
                                (_engine.HasAnyCloudAiKey || _engine.PhiSilicaState == 0))
                            {
                                _aiPrefetchTimer.Stop();
                                _aiPrefetchTimer.Start();
                            }
                        }
                    }
                }
            }
            else if (_cachedUia.IsEditable && !string.IsNullOrEmpty(_cachedUia.SelectedText))
            {
                if (_cachedUia.SelectedText != _activeSourceText)
                {
                    _activeSourceText = _cachedUia.SelectedText;
                    _replaceMode = 0;
                    _sourceLabel = "Selected Text";
                    _lastActivityUtc = DateTime.UtcNow;
                    TriggerAnalysis(_activeSourceText, false, false);
                }
            }
            else if (_cachedUia.IsEditable && string.IsNullOrEmpty(_activeSourceText) &&
                     !string.IsNullOrEmpty(_cachedUia.ControlValueText) && _cachedUia.ControlValueText.Length <= 1500 &&
                     _cachedUia.SupportsValueSet)
            {
                _activeSourceText = _cachedUia.ControlValueText;
                _replaceMode = 3;
                _sourceLabel = "Full Box";
                _activeUiaElement = _cachedUia.Element;
                _activeSupportsValueSet = _cachedUia.SupportsValueSet;
                TriggerAnalysis(_activeSourceText, false, (_engine.Settings.AiProvider != "ai_only"));
            }

            if (_dismissedForCurrentFocus) return;

            // 3. Determine whether the floating widget should be visible above the cursor
            bool hasAnchor = ResolveBestCaretAnchor(st, _cachedUia, false);
            bool userWantsToType = ShowOnTypingAndFocus &&
                                   (st.is_typing_active == 1 || _cachedUia.IsEditable);
            bool hasRecentSelection = (_replaceMode == 0 && !string.IsNullOrEmpty(_activeSourceText) &&
                                       (DateTime.UtcNow - _lastActivityUtc).TotalSeconds < 18);

            if (_isExpanded || _isCloseMenuOpen)
            {
                EnsureVisibleAtAnchor();
                return;
            }

            if (hasAnchor && (userWantsToType || hasRecentSelection))
            {
                if (st.is_typing_active == 1 || _cachedUia.IsEditable)
                {
                    _lastActivityUtc = DateTime.UtcNow;
                }

                if ((DateTime.UtcNow - _lastActivityUtc).TotalSeconds <= 16 || IsMouseOver)
                {
                    EnsureVisibleAtAnchor();
                }
                else if (Visibility == Visibility.Visible)
                {
                    HideOverlay();
                }
            }
            else if (Visibility == Visibility.Visible && !IsMouseOver && (DateTime.UtcNow - _lastActivityUtc).TotalSeconds > 6)
            {
                HideOverlay();
            }
        }

        private bool ResolveBestCaretAnchor(GvMonitorState st, UiaInspectionResult uia, bool allowMouseFallback)
        {
            int cx = 0, cy = 0, ch = 20;
            bool found = false;

            if (st.caret_source == 1 || st.caret_source == 2)
            {
                cx = st.caret_x;
                cy = st.caret_y;
                ch = Math.Max(16, st.caret_h);
                found = true;
            }
            else if (uia != null && uia.HasCaretRect)
            {
                cx = uia.CaretX;
                cy = uia.CaretY;
                ch = Math.Max(16, uia.CaretH);
                found = true;
            }
            else if (st.caret_source == 4 &&
                     (st.is_typing_active == 1 || st.has_selection_event == 1 ||
                      (uia != null && uia.IsEditable) ||
                      (_replaceMode == 0 && !string.IsNullOrEmpty(_activeSourceText))))
            {
                cx = st.caret_x;
                cy = st.caret_y;
                ch = Math.Max(16, st.caret_h);
                found = true;
            }
            else if (allowMouseFallback)
            {
                cx = st.mouse_x;
                cy = st.mouse_y - 8;
                ch = 20;
                found = true;
            }

            if (found && !_isExpanded && !_isCloseMenuOpen)
            {
                _anchorCaretX = cx;
                _anchorCaretY = cy;
                _anchorCaretH = ch;
            }
            else if (found && _anchorCaretX == 0 && _anchorCaretY == 0)
            {
                _anchorCaretX = cx;
                _anchorCaretY = cy;
                _anchorCaretH = ch;
            }
            return found;
        }

        private void EnsureVisibleAtAnchor()
        {
            if (_anchorCaretX == 0 && _anchorCaretY == 0) return;
            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
                UpdateLayout();
            }
            PositionAbovePhysicalPoint(_anchorCaretX, _anchorCaretY, _anchorCaretH, false);
        }

        private void PositionAbovePhysicalPoint(int caretPhysX, int caretPhysY, int caretPhysH, bool forceMove)
        {
            if (_hwnd == IntPtr.Zero) return;

            double scaleX = 1.0, scaleY = 1.0;
            PresentationSource ps = PresentationSource.FromVisual(this);
            if (ps != null && ps.CompositionTarget != null)
            {
                Matrix m = ps.CompositionTarget.TransformToDevice;
                scaleX = m.M11;
                scaleY = m.M22;
            }

            int winPhysW = Math.Max(90, (int)Math.Round(ActualWidth * scaleX));
            int winPhysH = Math.Max(36, (int)Math.Round(ActualHeight * scaleY));

            NativeBridge.RECT work = NativeBridge.GetWorkAreaForPhysicalPoint(caretPhysX, caretPhysY);

            int targetX = caretPhysX - 16;
            int targetY = caretPhysY - winPhysH - VerticalOffsetPx;

            // Clamp horizontally inside monitor working area
            if (targetX + winPhysW > work.Right - 8) targetX = work.Right - winPhysW - 8;
            if (targetX < work.Left + 8) targetX = work.Left + 8;

            // Always prefer ABOVE cursor; flip below cursor only if clipping top of screen
            if (targetY < work.Top + 4)
            {
                targetY = caretPhysY + caretPhysH + 8;
            }
            if (targetY + winPhysH > work.Bottom - 6)
            {
                targetY = work.Bottom - winPhysH - 6;
            }

            if (forceMove || Math.Abs(targetX - _lastMovedX) > 3 || Math.Abs(targetY - _lastMovedY) > 3)
            {
                _lastMovedX = targetX;
                _lastMovedY = targetY;
                NativeBridge.MoveWindowPhysicalNoActivate(_hwnd, targetX, targetY);
            }
        }

        private async void TriggerAnalysis(string text, bool force, bool fastBadgeOnly)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                UpdatePillAppearance(null);
                return;
            }
            if (_engine.Settings.AiProvider == "ai_only")
            {
                fastBadgeOnly = false;
            }
            bool needsDeepUpgrade = (!fastBadgeOnly && _lastAnalysisWasFast);
            if (!force && !needsDeepUpgrade && text == _lastAnalyzedText && _lastAnalysisWasFast == fastBadgeOnly) return;

            int mySeq = ++_analysisSeq;
            _lastAnalyzedText = text;
            _lastAnalysisWasFast = fastBadgeOnly;
            AnalyzeResponse res = await _engine.AnalyzeAsync(text, fastBadgeOnly);
            if (mySeq != _analysisSeq && fastBadgeOnly)
            {
                return;
            }
            _latestAnalysis = res;
            _cardSourceBadge.Text = "· " + _sourceLabel;
            UpdatePillAppearance(res);
            if (_isExpanded)
            {
                RefreshActiveTabOutput();
            }
        }

        private void UpdatePillAppearance(AnalyzeResponse res)
        {
            if (_isVoiceRecording || _voiceBusy || _isReadingAloud) return;

            if (res == null || string.IsNullOrEmpty(_activeSourceText))
            {
                _pillBadgeBorder.Background = Emerald;
                _pillRoot.BorderBrush = Emerald;
                _pillBadgeText.Text = "✓";
                _pillSummaryText.Text = string.Empty;
                _pillSummaryText.Visibility = Visibility.Collapsed;
                _pillQuickFixBtn.Visibility = Visibility.Collapsed;
                return;
            }

            if (res.IssueCount > 0)
            {
                _pillBadgeBorder.Background = Coral;
                _pillRoot.BorderBrush = Coral;
                _pillBadgeText.Text = res.IssueCount.ToString();
                _pillSummaryText.Text = res.QuickSummary;
                _pillSummaryText.Visibility = Visibility.Visible;
                _pillQuickFixBtn.Visibility = Visibility.Visible;
                _tabCorrectBtn.Content = string.Format("✓ Correct ({0})", res.IssueCount);
            }
            else
            {
                _pillBadgeBorder.Background = Emerald;
                _pillRoot.BorderBrush = Emerald;
                _pillBadgeText.Text = "✓";
                _pillSummaryText.Text = string.Empty;
                _pillSummaryText.Visibility = Visibility.Collapsed;
                _pillQuickFixBtn.Visibility = Visibility.Collapsed;
                _tabCorrectBtn.Content = "✓ Correct";
            }
        }

        private void RefreshActiveTabOutput()
        {
            if (string.IsNullOrEmpty(_activeSourceText) || _activeSourceText.Trim().Length == 0)
            {
                _diffPreviewBlock.Inlines.Clear();
                _diffPreviewBlock.Inlines.Add(new Run("Select or type words anywhere above your cursor, then click Grab or choose an option.")
                {
                    Foreground = TextMuted
                });
                _issuesWrap.Children.Clear();
                _outputPreviewBox.Text = "Select or type a word or sentence first.";
                _dictContentStack.Children.Clear();
                _dictContentStack.Children.Add(new TextBlock
                {
                    Text = "Select or type a word or sentence anywhere to see AI Persian translations, synonyms, antonyms, and example sentences.",
                    Foreground = TextMuted,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap
                });
                _currentOutputText = string.Empty;
                return;
            }

            if (_activeTab == "correct")
            {
                RenderCorrectTab(_latestAnalysis);
            }
            else if (_activeTab == "reword")
            {
                RunRewordTransform();
            }
            else if (_activeTab == "format")
            {
                RunFormatTransform();
            }
            else if (_activeTab == "dictionary")
            {
                RunDictionaryLookup(false);
            }
        }

        private static bool ContainsPersianText(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '\u0600' && c <= '\u06FF') || (c >= '\uFB50' && c <= '\uFEFF'))
                {
                    return true;
                }
            }
            return false;
        }

        private void RenderCorrectTab(AnalyzeResponse res)
        {
            _diffPreviewBlock.Inlines.Clear();
            _issuesWrap.Children.Clear();

            if (res == null) return;
            _currentOutputText = res.Corrected;
            _diffPreviewBlock.FlowDirection = ContainsPersianText(_currentOutputText)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
            _cardTimingBadge.Text = string.Format("{0} · {1:0.0} ms", res.Engine, res.ElapsedMs);

            if (res.Diff != null && res.Diff.Count > 0)
            {
                for (int i = 0; i < res.Diff.Count; i++)
                {
                    DiffSegment seg = res.Diff[i];
                    if (seg.Op == "del")
                    {
                        Run r = new Run(seg.Text);
                        r.Foreground = CoralSoft;
                        r.TextDecorations = TextDecorations.Strikethrough;
                        _diffPreviewBlock.Inlines.Add(r);
                    }
                    else if (seg.Op == "ins")
                    {
                        Run r = new Run(seg.Text);
                        r.Foreground = EmeraldSoft;
                        r.FontWeight = FontWeights.Bold;
                        r.Background = EmeraldDark;
                        _diffPreviewBlock.Inlines.Add(r);
                    }
                    else
                    {
                        _diffPreviewBlock.Inlines.Add(new Run(seg.Text) { Foreground = TextPrimary });
                    }
                }
            }
            else
            {
                _diffPreviewBlock.Inlines.Add(new Run(res.Corrected) { Foreground = TextPrimary });
            }

            if (res.Issues != null && res.Issues.Count > 0)
            {
                for (int i = 0; i < Math.Min(6, res.Issues.Count); i++)
                {
                    IssueItem iss = res.Issues[i];
                    Border tag = new Border
                    {
                        Background = BgChip,
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 5, 4),
                        ToolTip = iss.Explanation
                    };
                    tag.Child = new TextBlock
                    {
                        Text = string.Format("{0}: {1} → {2}", iss.Category, iss.Original, iss.Replacement),
                        Foreground = TextPrimary,
                        FontSize = 10.5
                    };
                    _issuesWrap.Children.Add(tag);
                }
            }
            else
            {
                _issuesWrap.Children.Add(new TextBlock
                {
                    Text = "✓ No spelling or grammar errors detected. Try Reword or Format!",
                    Foreground = EmeraldSoft,
                    FontSize = 11
                });
            }
        }

        private async void RunRewordTransform()
        {
            if (string.IsNullOrEmpty(_activeSourceText)) return;
            _outputPreviewBox.Text = "Rewording...";
            TransformResponse tr = await _engine.RewordAsync(_activeSourceText, _activeRewordMode, _rewordVariantIndex);
            _currentOutputText = tr.Result;
            _outputPreviewBox.FlowDirection = ContainsPersianText(_currentOutputText)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
            _outputPreviewBox.Text = tr.Result;
            _cardTimingBadge.Text = string.Format("{0} · {1:0.0} ms", tr.Engine, tr.ElapsedMs);
        }

        private async void RunCustomRewordTransform()
        {
            if (string.IsNullOrEmpty(_activeSourceText)) return;
            string customInstruction = (_customRewordInputBox != null && _customRewordInputBox.Foreground != TextMuted)
                ? (_customRewordInputBox.Text ?? string.Empty).Trim()
                : string.Empty;
            if (string.IsNullOrEmpty(customInstruction))
            {
                RunRewordTransform();
                return;
            }

            if (_allowInputBoxFocus)
            {
                _allowInputBoxFocus = false;
                NativeBridge.SetWindowActivable(_hwnd, false);
                if (_activeTargetHwnd != 0)
                {
                    try { NativeBridge.SetForegroundWindow(new IntPtr(_activeTargetHwnd)); } catch { }
                }
            }

            _outputPreviewBox.Text = "Applying your custom format / instruction...";
            _customRewordApplyBtn.IsEnabled = false;
            try
            {
                TransformResponse tr = await _engine.RewordAsync(_activeSourceText, "custom", 0, customInstruction);
                _currentOutputText = tr.Result;
                _outputPreviewBox.FlowDirection = ContainsPersianText(_currentOutputText)
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
                _outputPreviewBox.Text = tr.Result;
                _cardTimingBadge.Text = string.Format("{0} · {1:0.0} ms", tr.Engine, tr.ElapsedMs);
            }
            finally
            {
                _customRewordApplyBtn.IsEnabled = true;
            }
        }

        private async void RunFormatTransform()
        {
            if (string.IsNullOrEmpty(_activeSourceText)) return;
            TransformResponse tr = await _engine.FormatAsync(_activeSourceText, _activeFormatMode);
            _currentOutputText = tr.Result;
            _outputPreviewBox.FlowDirection = ContainsPersianText(_currentOutputText)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
            _outputPreviewBox.Text = tr.Result;
            _cardTimingBadge.Text = string.Format("{0} · {1:0.0} ms", tr.Engine, tr.ElapsedMs);
        }

        private static string ExtractCleanChipValue(string rawChip, bool sourceIsPersian)
        {
            if (string.IsNullOrEmpty(rawChip)) return string.Empty;
            string s = rawChip.Trim();
            int parenIdx = s.IndexOf(" (");
            if (parenIdx > 0 && s.EndsWith(")"))
            {
                string outside = s.Substring(0, parenIdx).Trim();
                string inside = s.Substring(parenIdx + 2, s.Length - parenIdx - 3).Trim();
                if (sourceIsPersian && ContainsPersianText(outside)) return outside;
                if (sourceIsPersian && ContainsPersianText(inside)) return inside;
                return outside;
            }
            return s;
        }

        private async void RunDictionaryLookup(bool forceRefresh)
        {
            if (string.IsNullOrEmpty(_activeSourceText) || _activeSourceText.Trim().Length == 0) return;
            string query = _activeSourceText.Trim();

            if (!forceRefresh && _latestDict != null && _lastDictQuery == query)
            {
                RenderDictionaryResult(_latestDict);
                return;
            }
            if (_dictInFlight) return;

            _dictInFlight = true;
            _dictContentStack.Children.Clear();
            _dictContentStack.Children.Add(new TextBlock
            {
                Text = "🤖 AI Dictionary looking up Persian translation, synonyms, antonyms & examples...",
                Foreground = EmeraldSoft,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4)
            });
            _cardTimingBadge.Text = "Querying AI Dictionary...";
            UpdateLayout();
            PositionAbovePhysicalPoint(_anchorCaretX, _anchorCaretY, _anchorCaretH, true);

            try
            {
                DictionaryResponse dr = await _engine.DictionaryLookupAsync(query);
                _latestDict = dr;
                _lastDictQuery = query;
                if (_activeTab == "dictionary")
                {
                    RenderDictionaryResult(dr);
                    UpdateLayout();
                    PositionAbovePhysicalPoint(_anchorCaretX, _anchorCaretY, _anchorCaretH, true);
                }
            }
            finally
            {
                _dictInFlight = false;
            }
        }

        private void RenderDictionaryResult(DictionaryResponse dr)
        {
            _dictContentStack.Children.Clear();
            if (dr == null) return;

            bool srcIsPersian = ContainsPersianText(_activeSourceText);
            if (srcIsPersian && !string.IsNullOrEmpty(dr.EnglishTranslation))
            {
                _currentOutputText = dr.EnglishTranslation;
            }
            else if (!string.IsNullOrEmpty(dr.PersianTranslation))
            {
                _currentOutputText = dr.PersianTranslation;
            }
            else
            {
                _currentOutputText = dr.Headword;
            }

            _cardTimingBadge.Text = string.Format("{0} · {1:0.0} ms", dr.Engine, dr.ElapsedMs);

            // 1. Headword + Part of Speech + Phonetic + Definition
            WrapPanel headRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 3) };
            headRow.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(dr.Headword) ? dr.Query : dr.Headword,
                Foreground = TextPrimary,
                FontWeight = FontWeights.Bold,
                FontSize = 13.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            if (!string.IsNullOrEmpty(dr.PartOfSpeech))
            {
                Border posPill = new Border
                {
                    Background = EmeraldDark,
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                posPill.Child = new TextBlock
                {
                    Text = dr.PartOfSpeech,
                    Foreground = EmeraldSoft,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                };
                headRow.Children.Add(posPill);
            }

            if (!string.IsNullOrEmpty(dr.Phonetic))
            {
                headRow.Children.Add(new TextBlock
                {
                    Text = dr.Phonetic,
                    Foreground = TextMuted,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            _dictContentStack.Children.Add(headRow);

            if (!string.IsNullOrEmpty(dr.Definition))
            {
                _dictContentStack.Children.Add(new TextBlock
                {
                    Text = dr.Definition,
                    Foreground = TextMuted,
                    FontSize = 11.2,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }

            // 2. Persian Translation Card (RTL) + Clickable Meaning Chips
            Border faCard = new Border
            {
                Background = EmeraldTint,
                BorderBrush = Emerald,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 0, 0, 7)
            };
            StackPanel faStack = new StackPanel();
            faStack.Children.Add(new TextBlock
            {
                Text = "PERSIAN TRANSLATION · ترجمه فارسی",
                Foreground = EmeraldSoft,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });

            if (!string.IsNullOrEmpty(dr.PersianTranslation))
            {
                TextBlock faMain = new TextBlock
                {
                    Text = dr.PersianTranslation,
                    Foreground = TextPrimary,
                    FontSize = 13.5,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    FlowDirection = FlowDirection.RightToLeft,
                    Cursor = Cursors.Hand,
                    ToolTip = "Click to select this Persian translation for Copy / Replace"
                };
                string mainFaCopy = dr.PersianTranslation;
                faMain.MouseLeftButtonUp += delegate
                {
                    _currentOutputText = mainFaCopy;
                    _cardTimingBadge.Text = "Selected Persian translation ✓";
                };
                faStack.Children.Add(faMain);
            }

            if (srcIsPersian && !string.IsNullOrEmpty(dr.EnglishTranslation))
            {
                TextBlock enSub = new TextBlock
                {
                    Text = "English: " + dr.EnglishTranslation,
                    Foreground = EmeraldSoft,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0)
                };
                faStack.Children.Add(enSub);
            }

            if (dr.PersianMeanings != null && dr.PersianMeanings.Count > 1)
            {
                WrapPanel meanWrap = new WrapPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    Margin = new Thickness(0, 5, 0, 0)
                };
                for (int i = 0; i < dr.PersianMeanings.Count; i++)
                {
                    string mVal = dr.PersianMeanings[i];
                    Button mChip = CreateSmallButton(mVal, BgChip, TextPrimary);
                    mChip.FontSize = 10.8;
                    mChip.Padding = new Thickness(7, 2, 7, 2);
                    mChip.Margin = new Thickness(0, 0, 4, 3);
                    mChip.ToolTip = "Click to select \"" + mVal + "\" for Copy / Replace";
                    mChip.Click += delegate
                    {
                        _currentOutputText = ExtractCleanChipValue(mVal, true);
                        _cardTimingBadge.Text = "Selected: " + _currentOutputText;
                    };
                    meanWrap.Children.Add(mChip);
                }
                faStack.Children.Add(meanWrap);
            }
            faCard.Child = faStack;
            _dictContentStack.Children.Add(faCard);

            // 3. Synonyms (Syn) & Antonyms (Ant)
            if (dr.Synonyms != null && dr.Synonyms.Count > 0)
            {
                _dictContentStack.Children.Add(new TextBlock
                {
                    Text = "SYNONYMS · مترادف‌ها (click chip to select)",
                    Foreground = EmeraldSoft,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 3)
                });
                WrapPanel synWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
                for (int i = 0; i < dr.Synonyms.Count; i++)
                {
                    string synRaw = dr.Synonyms[i];
                    Button sChip = CreateSmallButton(synRaw, EmeraldDark, EmeraldSoft);
                    sChip.FontSize = 10.8;
                    sChip.Padding = new Thickness(7, 2, 7, 2);
                    sChip.Margin = new Thickness(0, 0, 4, 4);
                    sChip.Click += delegate
                    {
                        _currentOutputText = ExtractCleanChipValue(synRaw, srcIsPersian);
                        _cardTimingBadge.Text = "Selected Syn: " + _currentOutputText;
                    };
                    synWrap.Children.Add(sChip);
                }
                _dictContentStack.Children.Add(synWrap);
            }

            if (dr.Antonyms != null && dr.Antonyms.Count > 0)
            {
                _dictContentStack.Children.Add(new TextBlock
                {
                    Text = "ANTONYMS · متضادها",
                    Foreground = CoralSoft,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 3)
                });
                WrapPanel antWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                for (int i = 0; i < dr.Antonyms.Count; i++)
                {
                    string antRaw = dr.Antonyms[i];
                    Button aChip = CreateSmallButton(antRaw, CoralDark, CoralSoft);
                    aChip.FontSize = 10.8;
                    aChip.Padding = new Thickness(7, 2, 7, 2);
                    aChip.Margin = new Thickness(0, 0, 4, 4);
                    aChip.Click += delegate
                    {
                        _currentOutputText = ExtractCleanChipValue(antRaw, srcIsPersian);
                        _cardTimingBadge.Text = "Selected Ant: " + _currentOutputText;
                    };
                    antWrap.Children.Add(aChip);
                }
                _dictContentStack.Children.Add(antWrap);
            }

            // 4. Bilingual Example Sentences
            if (dr.Examples != null && dr.Examples.Count > 0)
            {
                _dictContentStack.Children.Add(new TextBlock
                {
                    Text = "EXAMPLE SENTENCES · جمله‌های نمونه",
                    Foreground = TextMuted,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 1, 0, 3)
                });
                for (int i = 0; i < dr.Examples.Count; i++)
                {
                    DictionaryExampleItem ex = dr.Examples[i];
                    Border exBox = new Border
                    {
                        Background = BgDark,
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(8, 5, 8, 5),
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    StackPanel exStack = new StackPanel();
                    if (!string.IsNullOrEmpty(ex.En))
                    {
                        exStack.Children.Add(new TextBlock
                        {
                            Text = "• " + ex.En,
                            Foreground = TextPrimary,
                            FontSize = 11.2,
                            TextWrapping = TextWrapping.Wrap,
                            FlowDirection = FlowDirection.LeftToRight
                        });
                    }
                    if (!string.IsNullOrEmpty(ex.Fa))
                    {
                        exStack.Children.Add(new TextBlock
                        {
                            Text = ex.Fa,
                            Foreground = EmeraldSoft,
                            FontSize = 11.2,
                            TextWrapping = TextWrapping.Wrap,
                            FlowDirection = FlowDirection.RightToLeft,
                            Margin = new Thickness(0, 2, 0, 0)
                        });
                    }
                    exBox.Child = exStack;
                    _dictContentStack.Children.Add(exBox);
                }
            }
        }

        private void OnCopyClicked(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentOutputText)) return;
            try
            {
                Clipboard.SetText(_currentOutputText);
                _copyBtn.Content = "✓ Copied";
                DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                t.Tick += delegate { t.Stop(); _copyBtn.Content = "Copy"; };
                t.Start();
            }
            catch { }
        }

        private void OnReplaceClicked(object sender, RoutedEventArgs e)
        {
            ApplyReplacementToTarget();
        }

        private void ApplyReplacementToTarget()
        {
            if (string.IsNullOrEmpty(_currentOutputText)) return;

            if (_allowInputBoxFocus)
            {
                _allowInputBoxFocus = false;
                NativeBridge.SetWindowActivable(_hwnd, false);
                if (_activeTargetHwnd != 0)
                {
                    try { NativeBridge.SetForegroundWindow(new IntPtr(_activeTargetHwnd)); } catch { }
                }
            }

            // Always stream direct Win32 KEYEVENTF_UNICODE events via grammerv_native.dll
            // (eliminates Edge/Chromium clipboard paste races AND broken React/DOM UIA SetValue calls)
            int mode = (_replaceMode == 4) ? 3 : _replaceMode;
            if (NativeBridge.IsNativeLoaded)
            {
                NativeBridge.gv_replace_text(_activeTargetHwnd, _currentOutputText, mode, _typedBackspaceCount);
            }

            // Update source buffer to match replaced text so user can immediately chain another format/tone
            _activeSourceText = _currentOutputText;
            _typedBackspaceCount = _currentOutputText.Length;
            if (mode == 3)
            {
                _replaceMode = 3;
                _sourceLabel = "Full Box";
            }
            else
            {
                _replaceMode = 1;
                _sourceLabel = "Replaced Text";
            }
            _lastAnalyzedText = _currentOutputText;
            TriggerAnalysis(_activeSourceText, true, true);

            _replaceBtn.Content = "✓ Replaced!";
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            t.Tick += delegate
            {
                t.Stop();
                _replaceBtn.Content = "✨ Replace at Cursor";
                if (_isExpanded) SetExpanded(false);
            };
            t.Start();
        }
    }
}
