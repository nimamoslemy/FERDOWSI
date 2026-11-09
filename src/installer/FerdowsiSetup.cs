using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FerdowsiInstaller
{
    public class SetupWindow : Window
    {
        private TextBox _pathBox;
        private CheckBox _chkDesktop;
        private CheckBox _chkStartMenu;
        private CheckBox _chkStartup;
        private CheckBox _chkLaunchNow;
        private TextBlock _statusText;
        private Button _installBtn;

        private static readonly SolidColorBrush BgWindow = MakeBrush(11, 17, 32);
        private static readonly SolidColorBrush BgCard = MakeBrush(20, 29, 49);
        private static readonly SolidColorBrush BgInput = MakeBrush(15, 23, 42);
        private static readonly SolidColorBrush BgBtn = MakeBrush(51, 65, 85);
        private static readonly SolidColorBrush Emerald = MakeBrush(16, 185, 129);
        private static readonly SolidColorBrush EmeraldSoft = MakeBrush(110, 231, 183);
        private static readonly SolidColorBrush CoralSoft = MakeBrush(252, 165, 165);
        private static readonly SolidColorBrush TextLight = MakeBrush(248, 250, 252);
        private static readonly SolidColorBrush TextSub = MakeBrush(148, 163, 184);
        private static readonly SolidColorBrush BorderCol = MakeBrush(51, 65, 85);

        [STAThread]
        public static int Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--verify-package", StringComparison.OrdinalIgnoreCase))
                {
                    return VerifyEmbeddedPayload();
                }
                if (string.Equals(args[i], "--uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    return RunInteractiveUninstall();
                }
            }

            string exeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            if (exeName != null && exeName.IndexOf("Uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return RunInteractiveUninstall();
            }

            Application app = new Application();
            SetupWindow win = new SetupWindow();
            app.Run(win);
            return 0;
        }

        private static int VerifyEmbeddedPayload()
        {
            string[] required = new string[]
            {
                "payload_FERDOWSI.exe",
                "payload_grammerv_native.dll",
                "payload_nlp_engine.py",
                "payload_ferdowsi_flag.png",
                "payload_FERDOWSI.ico",
                "payload_FERDOWSI.png",
                "payload_AppxManifest.xml",
                "payload_enable_phi_silica.ps1",
                "payload_README.md",
                "payload_LICENSE.txt"
            };
            Assembly asm = Assembly.GetExecutingAssembly();
            for (int i = 0; i < required.Length; i++)
            {
                using (Stream s = asm.GetManifestResourceStream(required[i]))
                {
                    if (s == null || s.Length == 0)
                    {
                        Console.Error.WriteLine("MISSING_RESOURCE: " + required[i]);
                        return 2;
                    }
                }
            }
            Console.WriteLine("INSTALLER_PAYLOAD_VERIFIED_OK (" + required.Length + " resources)");
            return 0;
        }

        private static int RunInteractiveUninstall()
        {
            MessageBoxResult confirm = MessageBox.Show(
                "Are you sure you want to uninstall FERDOWSI from your computer?\n\nآیا مطمئن هستید که می‌خواهید برنامه FERDOWSI را حذف نصب کنید؟",
                "Uninstall FERDOWSI",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return 0;

            try
            {
                Process[] running = Process.GetProcessesByName("FERDOWSI");
                for (int i = 0; i < running.Length; i++)
                {
                    try { running[i].Kill(); running[i].WaitForExit(1500); } catch { }
                }

                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FERDOWSI.lnk");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FERDOWSI");
                if (Directory.Exists(startMenuDir)) Directory.Delete(startMenuDir, true);

                try
                {
                    using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (runKey != null) runKey.DeleteValue("FERDOWSI", false);
                    }
                }
                catch { }

                string installDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                try
                {
                    using (RegistryKey uninst = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FERDOWSI", false))
                    {
                        if (uninst != null && uninst.GetValue("InstallLocation") != null)
                        {
                            installDir = uninst.GetValue("InstallLocation").ToString();
                        }
                    }
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FERDOWSI", false);
                }
                catch { }

                if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                {
                    ProcessStartInfo cleanupPsi = new ProcessStartInfo("cmd.exe",
                        "/C ping 127.0.0.1 -n 2 > nul & rmdir /S /Q \"" + installDir + "\"");
                    cleanupPsi.CreateNoWindow = true;
                    cleanupPsi.UseShellExecute = false;
                    Process.Start(cleanupPsi);
                }

                MessageBox.Show("FERDOWSI has been uninstalled cleanly.", "FERDOWSI Uninstaller", MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Uninstall encountered an error: " + ex.Message, "FERDOWSI Uninstaller", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 1;
            }
        }

        private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
        {
            SolidColorBrush br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        public SetupWindow()
        {
            Title = "FERDOWSI v1.0.0 — Setup Wizard";
            Width = 610;
            Height = 490;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BgWindow;

            ImageSource appIcon = LoadEmbeddedPng("payload_FERDOWSI.png");
            if (appIcon != null) Icon = appIcon;

            BuildWizardUi();
        }

        private static ImageSource LoadEmbeddedPng(string resName)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(resName))
                {
                    if (s == null) return null;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        ms.Position = 0;
                        BitmapImage bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private void BuildWizardUi()
        {
            StackPanel root = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };

            // Header with F icon + Flag + FERDOWSI Title
            StackPanel headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            Border logo = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(12),
                Background = Emerald,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            logo.Child = new TextBlock
            {
                Text = "F",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerRow.Children.Add(logo);

            ImageSource flagSrc = LoadEmbeddedPng("payload_ferdowsi_flag.png");
            if (flagSrc != null)
            {
                Image flagImg = new Image
                {
                    Source = flagSrc,
                    Height = 40,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(flagImg, BitmapScalingMode.HighQuality);
                headerRow.Children.Add(flagImg);
            }

            StackPanel titleCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleCol.Children.Add(new TextBlock
            {
                Text = "Install FERDOWSI (v1.0.0)",
                Foreground = TextLight,
                FontSize = 20,
                FontWeight = FontWeights.Bold
            });
            titleCol.Children.Add(new TextBlock
            {
                Text = "Above-Cursor English, Persian & Finglish Writing Assistant & AI Dictionary",
                Foreground = TextSub,
                FontSize = 12
            });
            titleCol.Children.Add(new TextBlock
            {
                Text = "دستیار هوشمند نگارش انگلیسی، فارسی، فینگلیش و دیکشنری بالای مکان‌نما",
                Foreground = EmeraldSoft,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            });
            headerRow.Children.Add(titleCol);
            root.Children.Add(headerRow);

            // Install Options Card
            Border card = new Border
            {
                Background = BgCard,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            StackPanel cardStack = new StackPanel();

            cardStack.Children.Add(new TextBlock
            {
                Text = "Installation Folder (پوشه نصب برنامه):",
                Foreground = TextLight,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "FERDOWSI");

            DockPanel pathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            Button browseBtn = MakeButton("Browse...", BgBtn, TextLight);
            browseBtn.Margin = new Thickness(8, 0, 0, 0);
            browseBtn.Click += OnBrowseClicked;
            DockPanel.SetDock(browseBtn, Dock.Right);
            pathRow.Children.Add(browseBtn);

            _pathBox = new TextBox
            {
                Text = defaultDir,
                Background = BgInput,
                Foreground = TextLight,
                CaretBrush = EmeraldSoft,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 12.5
            };
            pathRow.Children.Add(_pathBox);
            cardStack.Children.Add(pathRow);

            _chkDesktop = MakeCheck("Create Desktop shortcut (FERDOWSI)", true);
            _chkStartMenu = MakeCheck("Create Start Menu shortcut & register in Windows Installed Apps", true);
            _chkStartup = MakeCheck("Run FERDOWSI automatically in System Tray when Windows starts", false);
            _chkLaunchNow = MakeCheck("Launch FERDOWSI immediately after installation completes", true);

            cardStack.Children.Add(_chkDesktop);
            cardStack.Children.Add(_chkStartMenu);
            cardStack.Children.Add(_chkStartup);
            cardStack.Children.Add(_chkLaunchNow);

            _statusText = new TextBlock
            {
                Text = "Ready to install FERDOWSI (64-bit Windows 10 / 11). No personal API keys bundled; configure your own optional Gemini key in Settings after launch.",
                Foreground = TextSub,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            cardStack.Children.Add(_statusText);

            card.Child = cardStack;
            root.Children.Add(card);

            // Bottom Action Bar + Author Link
            DockPanel bottomBar = new DockPanel();

            StackPanel actionBtns = new StackPanel { Orientation = Orientation.Horizontal };
            Button cancelBtn = MakeButton("Cancel", BgBtn, TextLight);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += delegate { Close(); };

            _installBtn = MakeButton("✨ Install FERDOWSI", Emerald, Brushes.White);
            _installBtn.Padding = new Thickness(18, 8, 18, 8);
            _installBtn.Click += OnInstallClicked;

            actionBtns.Children.Add(cancelBtn);
            actionBtns.Children.Add(_installBtn);
            DockPanel.SetDock(actionBtns, Dock.Right);
            bottomBar.Children.Add(actionBtns);

            StackPanel creditCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            creditCol.Children.Add(new TextBlock
            {
                Text = "GitHub Page:",
                Foreground = TextLight,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });
            TextBlock ghText = new TextBlock { FontSize = 11.5, Foreground = TextSub };
            Hyperlink hl = new Hyperlink(new Run("https://github.com/nimamoslemy"));
            hl.Foreground = EmeraldSoft;
            hl.Click += delegate
            {
                try { Process.Start("https://github.com/nimamoslemy"); } catch { }
            };
            ghText.Inlines.Add(hl);
            creditCol.Children.Add(ghText);
            bottomBar.Children.Add(creditCol);

            root.Children.Add(bottomBar);
            Content = root;
        }

        private void OnBrowseClicked(object sender, RoutedEventArgs e)
        {
            using (Forms.FolderBrowserDialog dlg = new Forms.FolderBrowserDialog())
            {
                dlg.Description = "Choose installation folder for FERDOWSI";
                dlg.SelectedPath = _pathBox.Text;
                if (dlg.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
                {
                    string chosen = dlg.SelectedPath;
                    if (!chosen.EndsWith("FERDOWSI", StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = Path.Combine(chosen, "FERDOWSI");
                    }
                    _pathBox.Text = chosen;
                }
            }
        }

        private void OnInstallClicked(object sender, RoutedEventArgs e)
        {
            string targetDir = (_pathBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(targetDir))
            {
                _statusText.Text = "Please choose a valid installation folder.";
                _statusText.Foreground = CoralSoft;
                return;
            }

            _installBtn.IsEnabled = false;
            _statusText.Text = "Installing FERDOWSI files...";
            _statusText.Foreground = EmeraldSoft;

            try
            {
                Process[] running = Process.GetProcessesByName("FERDOWSI");
                for (int i = 0; i < running.Length; i++)
                {
                    try { running[i].Kill(); running[i].WaitForExit(1500); } catch { }
                }

                Directory.CreateDirectory(targetDir);
                Directory.CreateDirectory(Path.Combine(targetDir, "engine"));
                Directory.CreateDirectory(Path.Combine(targetDir, "assets"));

                ExtractTo("payload_FERDOWSI.exe", Path.Combine(targetDir, "FERDOWSI.exe"));
                ExtractTo("payload_grammerv_native.dll", Path.Combine(targetDir, "grammerv_native.dll"));
                ExtractTo("payload_nlp_engine.py", Path.Combine(targetDir, "engine", "nlp_engine.py"));
                ExtractTo("payload_ferdowsi_flag.png", Path.Combine(targetDir, "assets", "ferdowsi_flag.png"));
                ExtractTo("payload_FERDOWSI.ico", Path.Combine(targetDir, "FERDOWSI.ico"));
                ExtractTo("payload_FERDOWSI.png", Path.Combine(targetDir, "FERDOWSI.png"));
                ExtractTo("payload_AppxManifest.xml", Path.Combine(targetDir, "AppxManifest.xml"));
                ExtractTo("payload_enable_phi_silica.ps1", Path.Combine(targetDir, "enable_phi_silica.ps1"));
                ExtractTo("payload_README.md", Path.Combine(targetDir, "README.md"));
                ExtractTo("payload_LICENSE.txt", Path.Combine(targetDir, "LICENSE.txt"));

                string uninstallerPath = Path.Combine(targetDir, "Uninstall_FERDOWSI.exe");
                File.Copy(Assembly.GetExecutingAssembly().Location, uninstallerPath, true);

                string exePath = Path.Combine(targetDir, "FERDOWSI.exe");
                string icoPath = Path.Combine(targetDir, "FERDOWSI.ico");

                if (_chkDesktop.IsChecked == true)
                {
                    string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FERDOWSI.lnk");
                    CreateShortcut(deskLnk, exePath, targetDir, icoPath, "FERDOWSI — Above-Cursor Writing Assistant & AI Dictionary");
                }

                if (_chkStartMenu.IsChecked == true)
                {
                    string smDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FERDOWSI");
                    Directory.CreateDirectory(smDir);
                    CreateShortcut(Path.Combine(smDir, "FERDOWSI.lnk"), exePath, targetDir, icoPath, "FERDOWSI Writing Assistant");
                    CreateShortcut(Path.Combine(smDir, "Uninstall FERDOWSI.lnk"), uninstallerPath, targetDir, icoPath, "Uninstall FERDOWSI");
                    RegisterUninstallerInWindows(targetDir, exePath, uninstallerPath, icoPath);
                }

                try
                {
                    using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (runKey != null)
                        {
                            if (_chkStartup.IsChecked == true)
                            {
                                runKey.SetValue("FERDOWSI", "\"" + exePath + "\" --tray");
                            }
                            else
                            {
                                runKey.DeleteValue("FERDOWSI", false);
                            }
                        }
                    }
                }
                catch { }

                _statusText.Text = "✓ FERDOWSI v1.0.0 installed successfully to: " + targetDir;
                _statusText.Foreground = EmeraldSoft;

                if (_chkLaunchNow.IsChecked == true)
                {
                    ProcessStartInfo psi = new ProcessStartInfo(exePath);
                    psi.WorkingDirectory = targetDir;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }

                MessageBox.Show(
                    "FERDOWSI v1.0.0 has been installed successfully!\n\nFolder: " + targetDir + "\nHotkey: Alt + G (anywhere above your cursor)",
                    "FERDOWSI Setup Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                _statusText.Text = "Installation failed: " + ex.Message;
                _statusText.Foreground = CoralSoft;
                _installBtn.IsEnabled = true;
            }
        }

        private static void ExtractTo(string resourceName, string destPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(resourceName))
            {
                if (s == null) throw new InvalidOperationException("Missing embedded resource: " + resourceName);
                using (FileStream fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    s.CopyTo(fs);
                }
            }
        }

        private static void CreateShortcut(string lnkPath, string targetExe, string workDir, string iconPath, string description)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workDir });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
        }

        private static void RegisterUninstallerInWindows(string installDir, string exePath, string uninstallerPath, string iconPath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FERDOWSI"))
                {
                    if (key == null) return;
                    key.SetValue("DisplayName", "FERDOWSI — Above-Cursor Writing Assistant & AI Dictionary");
                    key.SetValue("DisplayVersion", "1.0.0");
                    key.SetValue("Publisher", "https://github.com/nimamoslemy");
                    key.SetValue("URLInfoAbout", "https://github.com/nimamoslemy");
                    key.SetValue("DisplayIcon", iconPath);
                    key.SetValue("InstallLocation", installDir);
                    key.SetValue("UninstallString", "\"" + uninstallerPath + "\" --uninstall");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private CheckBox MakeCheck(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = new TextBlock
                {
                    Text = text,
                    Foreground = TextLight,
                    FontSize = 12.5,
                    Margin = new Thickness(4, -1, 0, 0)
                },
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };
        }

        private Button MakeButton(string text, Brush bg, Brush fg)
        {
            Button btn = new Button
            {
                Content = text,
                Background = bg,
                Foreground = fg,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(13, 6, 13, 6),
                FontSize = 12.5,
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
