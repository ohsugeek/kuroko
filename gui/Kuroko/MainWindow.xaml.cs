using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace Kuroko;

/// <summary>
/// Kuroko メインウィンドウ。エンジンへ名前付きパイプで接続し、髪色・フィルタ値をライブ送信する。
/// プリセット（髪色/フル）の保存・呼び出し・削除も担う。
/// </summary>
public partial class MainWindow : Window
{
    private readonly EngineClient _engine = new();
    private readonly PresetStore _presets = new();
    private readonly EngineProcess _engineProc = new();
    private readonly PreviewReader _preview = new();
    private readonly SettingsStore _settings = new();
    private readonly VirtualCamWatcher _watcher = new();
    // 自動開始で起動したかどうか（自動停止の対象を自動開始したときだけに限るため）
    private bool _autoStarted;
    private CancellationTokenSource? _pendingAutoStop;
    private const int AutoStopGraceSeconds = 30;
    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _trayToggle;
    private WinForms.ToolStripMenuItem? _miShow;
    private WinForms.ToolStripMenuItem? _miStartBoot;
    private WinForms.ToolStripMenuItem? _miTray;
    private WinForms.ToolStripMenuItem? _miAuto;
    private WinForms.ToolStripMenuItem? _miVcam;
    private WinForms.ToolStripMenuItem? _miUpdate;
    private WinForms.ToolStripMenuItem? _miExit;
    private WinForms.ToolStripMenuItem? _miLang;
    private bool _connected;
    private bool _suppressSend;  // プリセット適用時など、まとめて同期するため個別送信を抑制する
    private int _cameraIndex = -1;
    private bool _running;
    private bool _reallyExit;
    private bool _startHidden;
    private PreviewWindow? _previewWindow;

    public MainWindow()
    {
        InitializeComponent();
        Logger.Info("MainWindow initialized");

        _presets.Load();
        _settings.Load();

        // 初期値設定でValueChangedが誤発火しないよう、ハンドラは生成後に接続する
        SatSlider.ValueChanged += (_, _) => OnSat();
        BriSlider.ValueChanged += (_, _) => OnBri();
        LiftSlider.ValueChanged += (_, _) => OnLift();
        HueSlider.ValueChanged += (_, _) => OnHue();
        ThresholdSlider.ValueChanged += (_, _) => OnThreshold();
        GuideSlider.ValueChanged += (_, _) => OnGuide();
        TolSlider.ValueChanged += (_, _) => OnTol();
        SoftSlider.ValueChanged += (_, _) => OnSoft();

        BuildColorChips();
        BuildFullPresetChips();

        _engine.ConnectionChanged += OnConnectionChanged;
        _engine.Start();

        _engineProc.Locate();
        SetupTray();
        InitCameraDefault();
        ApplySettings();
        ApplyLanguageTexts(); // 開始ボタン・状態表示などコード側テキストを現在言語で確定
        if (Environment.GetCommandLineArgs().Any(a => a == "--tray"))
        {
            _startHidden = true; // Windows起動時の自動開始はトレイに格納した状態で立ち上げる
        }
    }

    // ===== タスクトレイ常駐・設定・自動開始 =====

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon { Text = "Kuroko", Visible = true };
        try
        {
            var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/kuroko.ico"));
            if (res is not null)
            {
                _tray.Icon = new System.Drawing.Icon(res.Stream);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Tray icon load failed", ex);
        }

        var menu = new WinForms.ContextMenuStrip
        {
            Renderer = new KurokoMenuRenderer(),
            BackColor = System.Drawing.Color.FromArgb(0x21, 0x1D, 0x18),
            ForeColor = System.Drawing.Color.FromArgb(0xF4, 0xF0, 0xE9),
        };
        _miShow = new WinForms.ToolStripMenuItem(Loc.T("S_tray_show"), null, (_, _) => ShowFromTray());
        _trayToggle = new WinForms.ToolStripMenuItem(Loc.T("S_start"), null, (_, _) => ToggleFromTray());
        _miStartBoot = new WinForms.ToolStripMenuItem(Loc.T("S_tray_startboot")) { CheckOnClick = true };
        _miStartBoot.Click += (_, _) =>
        {
            _settings.Data.StartOnBoot = _miStartBoot.Checked;
            _settings.Save();
            StartupRegistry.Set(_miStartBoot.Checked);
        };
        _miTray = new WinForms.ToolStripMenuItem(Loc.T("S_tray_mintray")) { CheckOnClick = true };
        _miTray.Click += (_, _) => { _settings.Data.MinimizeToTray = _miTray.Checked; _settings.Save(); };
        _miAuto = new WinForms.ToolStripMenuItem(Loc.T("S_tray_autoact")) { CheckOnClick = true };
        _miAuto.Click += (_, _) => { _settings.Data.AutoActivate = _miAuto.Checked; _settings.Save(); ApplyAutoActivate(); };
        _miVcam = new WinForms.ToolStripMenuItem(Loc.T("S_vcam_setup"), null, (_, _) => SetupVirtualCamera(silentIfOk: false));
        _miUpdate = new WinForms.ToolStripMenuItem(Loc.T("S_tray_update"), null, async (_, _) =>
        {
            var msg = await Updater.CheckAndApplyAsync();
            if (msg is not null)
            {
                Dispatcher.Invoke(() => ConfirmDialog.Info(this, msg));
            }
        });
        _miExit = new WinForms.ToolStripMenuItem(Loc.T("S_tray_exit"), null, (_, _) => { _reallyExit = true; Close(); });

        // 言語切替サブメニュー（日本語 / English）
        _miLang = new WinForms.ToolStripMenuItem(Loc.T("S_lang_menu"));
        foreach (var opt in Loc.Available)
        {
            var item = new WinForms.ToolStripMenuItem(opt.Display) { Tag = opt.Code, Checked = opt.Code == Loc.Current };
            item.Click += (_, _) => ChangeLanguage(opt.Code);
            _miLang.DropDownItems.Add(item);
        }

        menu.Items.Add(_miShow);
        menu.Items.Add(_trayToggle);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_miStartBoot);
        menu.Items.Add(_miTray);
        menu.Items.Add(_miAuto);
        menu.Items.Add(_miLang);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_miVcam);
        menu.Items.Add(_miUpdate);
        menu.Items.Add(_miExit);
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    // 言語を切り替え、設定に保存し、コード側テキストを再適用する。
    private void ChangeLanguage(string code)
    {
        if (code == Loc.Current)
        {
            return;
        }
        _settings.Data.Language = code;
        _settings.Save();
        Loc.SetLanguage(code); // XAMLの{DynamicResource}は自動更新。コード側は下で再適用。
        ApplyLanguageTexts();
        Logger.Info($"Language changed to {code}");
    }

    // コードから設定しているテキスト（トレイ項目・状態表示・開始停止・チップ）を現在言語で再適用する。
    private void ApplyLanguageTexts()
    {
        if (_miShow is not null) _miShow.Text = Loc.T("S_tray_show");
        if (_miStartBoot is not null) _miStartBoot.Text = Loc.T("S_tray_startboot");
        if (_miTray is not null) _miTray.Text = Loc.T("S_tray_mintray");
        if (_miAuto is not null) _miAuto.Text = Loc.T("S_tray_autoact");
        if (_miVcam is not null) _miVcam.Text = Loc.T("S_vcam_setup");
        if (_miUpdate is not null) _miUpdate.Text = Loc.T("S_tray_update");
        if (_miExit is not null) _miExit.Text = Loc.T("S_tray_exit");
        if (_miLang is not null)
        {
            _miLang.Text = Loc.T("S_lang_menu");
            foreach (WinForms.ToolStripMenuItem item in _miLang.DropDownItems)
            {
                item.Checked = (string?)item.Tag == Loc.Current;
            }
        }
        var toggle = _running ? Loc.T("S_stop") : Loc.T("S_start");
        if (_trayToggle is not null) _trayToggle.Text = toggle;
        StartStopButton.Content = toggle;
        StatusText.Text = _connected ? Loc.T("S_status_connected") : Loc.T("S_status_disconnected");
        BuildColorChips();     // ツールチップ・組み込み色名を再生成
        BuildFullPresetChips();
        _previewWindow?.Activate(); // タイトルは{DynamicResource}で自動更新
    }

    // 仮想カメラ(UnityCapture)が未セットアップなら、初回だけ案内する。
    // 見送られたら以降は自動案内しない(トレイの「仮想カメラをセットアップ」から手動で可能)。
    private void MaybePromptVirtualCamera()
    {
        if (VirtualCamInstaller.IsInstalled() || _settings.Data.VcamPromptDeclined)
        {
            return;
        }
        if (!VirtualCamInstaller.BundlePresent())
        {
            Logger.Error("Skipping vcam prompt: bundle not present");
            return;
        }
        bool ok = ConfirmDialog.Confirm(this, Loc.T("S_vcam_prompt"), Loc.T("S_vcam_setup"));
        if (ok)
        {
            SetupVirtualCamera(silentIfOk: false);
        }
        else
        {
            _settings.Data.VcamPromptDeclined = true; // 見送りを記録して次回から案内しない
            _settings.Save();
        }
    }

    // 仮想カメラを登録(修復)する。UAC が入る。結果を通知する。
    private void SetupVirtualCamera(bool silentIfOk)
    {
        if (!VirtualCamInstaller.BundlePresent())
        {
            ConfirmDialog.Info(this, Loc.T("S_vcam_missing"));
            return;
        }
        bool ok = VirtualCamInstaller.Install();
        if (ok)
        {
            _settings.Data.VcamPromptDeclined = false;
            _settings.Save();
            if (!silentIfOk)
            {
                ConfirmDialog.Info(this, Loc.T("S_vcam_ok"));
            }
        }
        else
        {
            ConfirmDialog.Info(this, Loc.T("S_vcam_fail"));
        }
    }

    private void ApplySettings()
    {
        _miStartBoot!.Checked = _settings.Data.StartOnBoot;
        _miTray!.Checked = _settings.Data.MinimizeToTray;
        _miAuto!.Checked = _settings.Data.AutoActivate;
        ApplyAutoActivate();
    }

    private void ApplyAutoActivate()
    {
        if (_settings.Data.AutoActivate)
        {
            _watcher.ActiveChanged -= OnVCamActiveChanged;
            _watcher.ActiveChanged += OnVCamActiveChanged;
            _watcher.Start();
        }
        else
        {
            _watcher.Stop();
        }
    }

    // 仮想カメラを誰か(Zoom等)が開いたら自動で処理を開始し、使い終わったら自動で停止する。
    // 停止するのは「自動で開始したとき」だけ。手動で開始した(プレビューを見ている等)場合は勝手に止めない。
    private void OnVCamActiveChanged(bool active)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_settings.Data.AutoActivate)
            {
                return;
            }
            if (active)
            {
                _pendingAutoStop?.Cancel(); // 一瞬切れて戻った場合は停止予約を取り消す
                _pendingAutoStop = null;
                if (!_running)
                {
                    Logger.Info("Virtual camera consumer detected; auto-starting engine");
                    StartEngine();
                    _autoStarted = _running;
                }
            }
            else if (_running && _autoStarted)
            {
                ScheduleAutoStop();
            }
        });
    }

    // 会議の切り替えやカメラの入れ替えで一瞬コンシューマが消えることがあるため、
    // すぐには止めず猶予を置き、まだ使われていないことを再確認してから停止する。
    private void ScheduleAutoStop()
    {
        _pendingAutoStop?.Cancel();
        var cts = new CancellationTokenSource();
        _pendingAutoStop = cts;
        Logger.Info($"Virtual camera consumer gone; auto-stop scheduled in {AutoStopGraceSeconds}s");
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(AutoStopGraceSeconds), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Dispatcher.Invoke(() =>
            {
                if (cts.IsCancellationRequested || VirtualCamWatcher.IsConsumerActive())
                {
                    Logger.Info("Auto-stop cancelled; consumer is active again");
                    return;
                }
                if (_running && _autoStarted)
                {
                    Logger.Info("Auto-stopping engine (virtual camera no longer in use)");
                    StopEngine();
                    _autoStarted = false;
                }
            });
        }, cts.Token);
    }

    private void ToggleFromTray()
    {
        StartStopButton_Click(this, new RoutedEventArgs());
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_startHidden)
        {
            Hide();
        }
        else
        {
            // 画面表示後に、仮想カメラの初回セットアップを案内する(トレイ起動時は出さない)
            Dispatcher.BeginInvoke(new Action(MaybePromptVirtualCamera),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 「閉じたらトレイに格納」が有効なら、閉じるボタンでは終了せずトレイへ格納する
        if (_settings.Data.MinimizeToTray && !_reallyExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _watcher.Stop();
        _preview.Stop();
        _engineProc.Stop();
        _engine.Dispose();
        _tray?.Dispose();
        _previewWindow?.Close();
        base.OnClosing(e);
    }

    // ===== カメラ選択・エンジン起動/停止・プレビュー =====

    // 既定カメラ: 前回選択を優先し、なければ仮想カメラを避けて最初の実カメラを選ぶ
    private void InitCameraDefault()
    {
        var cams = CameraEnumerator.List();
        var saved = _settings.Data.CameraName;
        if (!string.IsNullOrEmpty(saved))
        {
            var match = cams.FirstOrDefault(c => c.Name == saved);
            if (match is not null)
            {
                SetCameraLabel(match.Index, match.Name);
                Logger.Info($"Camera restored: index={_cameraIndex} ({saved})");
                return;
            }
        }
        foreach (var c in cams)
        {
            if (c.Name.Contains("Unity Video Capture", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Contains("OBS Virtual", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            SetCameraLabel(c.Index, c.Name);
            break;
        }
        if (_cameraIndex < 0 && cams.Count > 0)
        {
            SetCameraLabel(cams[0].Index, cams[0].Name);
        }
        Logger.Info($"Default camera: index={_cameraIndex} ({CameraNameText.Text})");
    }

    private void SetCameraLabel(int index, string name)
    {
        _cameraIndex = index;
        CameraNameText.Text = name;
        CameraNameText.Foreground = HexToBrush("#F4F0E9");
    }

    // タイトルバーの言語ボタン: 日本語/English を選ぶ小さなポップアップ（トレイの言語切替と同機能）
    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        LangList.Children.Clear();
        foreach (var opt in Loc.Available)
        {
            bool current = opt.Code == Loc.Current;
            var row = new Border
            {
                Padding = new Thickness(12, 9, 12, 9), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    // 現在の言語には先頭にチェックを付ける
                    Text = (current ? "✓  " : "     ") + opt.Display,
                    Foreground = HexToBrush(current ? "#F4F0E9" : "#A79F95"), FontSize = 13,
                },
            };
            row.MouseEnter += (_, _) => row.Background = HexToBrush("#322C25");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => { LangPopup.IsOpen = false; ChangeLanguage(opt.Code); };
            LangList.Children.Add(row);
        }
        LangPopup.IsOpen = true;
    }

    private void CameraSelector_Click(object sender, MouseButtonEventArgs e)
    {
        CameraList.Children.Clear();
        foreach (var c in CameraEnumerator.List())
        {
            int idx = c.Index;
            string name = c.Name;
            var row = new Border
            {
                Padding = new Thickness(12, 9, 12, 9), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Child = new TextBlock { Text = name, Foreground = HexToBrush("#F4F0E9"), FontSize = 13 },
            };
            row.MouseEnter += (_, _) => row.Background = HexToBrush("#322C25");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => SelectCamera(idx, name);
            CameraList.Children.Add(row);
        }
        CameraPopup.IsOpen = true;
    }

    private void SelectCamera(int index, string name)
    {
        CameraPopup.IsOpen = false;
        SetCameraLabel(index, name);
        _settings.Data.CameraName = name;  // 次回起動時に復元
        _settings.Save();
        Logger.Info($"Camera selected: index={index} ({name})");
        if (_running)
        {
            // カメラは「要再起動」パラメータ。選択カメラでエンジンを再起動して反映する
            _preview.Stop();
            _engineProc.Restart(_cameraIndex);
            StartPreview();
        }
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        // 手動操作した時点で自動停止の対象から外す（見ている最中に勝手に止まらないように）
        _autoStarted = false;
        _pendingAutoStop?.Cancel();
        _pendingAutoStop = null;
        if (!_running)
        {
            StartEngine();
        }
        else
        {
            StopEngine();
        }
    }

    private void StartEngine()
    {
        if (_cameraIndex < 0)
        {
            InitCameraDefault();
            if (_cameraIndex < 0)
            {
                ConfirmDialog.Info(this, Loc.T("S_msg_no_camera"));
                return;
            }
        }
        if (!_engineProc.Start(_cameraIndex))
        {
            ConfirmDialog.Info(this, Loc.T("S_msg_engine_failed"));
            return;
        }
        _running = true;
        StartStopButton.Content = Loc.T("S_stop");
        if (_trayToggle is not null) _trayToggle.Text = Loc.T("S_stop");
        StartPreview();
    }

    private void StopEngine()
    {
        _preview.Stop();
        PreviewImage.Source = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        _engineProc.Stop();
        _running = false;
        StartStopButton.Content = Loc.T("S_start");
        if (_trayToggle is not null) _trayToggle.Text = Loc.T("S_start");
    }

    // エンジンが共有メモリへ書き出す再着色フレームをプレビュー表示する（会議相手が見る映像と同一）
    private void StartPreview()
    {
        _preview.Start(bmp => Dispatcher.Invoke(() =>
        {
            PreviewImage.Source = bmp;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            _previewWindow?.SetFrame(bmp); // 拡大ウィンドウが開いていれば同じフレームを配信
        }));
    }

    // プレビューを独立ウィンドウで拡大表示する（リサイズ可）
    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewWindow is null)
        {
            _previewWindow = new PreviewWindow { Owner = this };
            _previewWindow.Closed += (_, _) => _previewWindow = null;
            _previewWindow.Show();
        }
        else
        {
            _previewWindow.Activate();
        }
    }

    // ---- スライダー値 → エンジンパラメータの換算 ----
    private double SatParam => SatSlider.Value / 50.0;
    private double BriParam => BriSlider.Value / 50.0;
    private double LiftParam => LiftSlider.Value / 100.0;
    private double HueParam => HueSlider.Value / 360.0;
    private double ThresholdParam => ThresholdSlider.Value / 100.0;
    private double GuideParam => GuideSlider.Value / 100.0;
    private double TolParam => TolSlider.Value / 100.0;
    private double SoftParam => SoftSlider.Value / 10.0;

    private void OnSat() { SatValue.Text = SatParam.ToString("0.00", CultureInfo.InvariantCulture); Send("sat", SatParam); }
    private void OnBri() { BriValue.Text = BriParam.ToString("0.00", CultureInfo.InvariantCulture); Send("bri", BriParam); }
    private void OnLift() { LiftValue.Text = LiftParam.ToString("0.00", CultureInfo.InvariantCulture); Send("lift", LiftParam); }
    private void OnHue() { HueValue.Text = ((int)HueSlider.Value).ToString(CultureInfo.InvariantCulture); Send("shift", HueParam); }
    private void OnThreshold() { ThresholdValue.Text = ThresholdParam.ToString("0.00", CultureInfo.InvariantCulture); Send("threshold", ThresholdParam); }
    private void OnGuide() { GuideValue.Text = GuideParam.ToString("0.00", CultureInfo.InvariantCulture); Send("guide", GuideParam); }
    private void OnTol() { TolValue.Text = TolParam.ToString("0.00", CultureInfo.InvariantCulture); Send("tol", TolParam); }
    private void OnSoft() { SoftValue.Text = SoftParam.ToString("0.0", CultureInfo.InvariantCulture); Send("soft", SoftParam); }

    private void Send(string name, double value)
    {
        if (_suppressSend)
        {
            return;
        }
        _engine.Send($"{name} {value.ToString("0.####", CultureInfo.InvariantCulture)}");
    }

    // ===== 髪色プリセット（チップ） =====

    private void BuildColorChips()
    {
        ColorChips.Children.Clear();
        foreach (var p in _presets.Data.ColorPresets)
        {
            var chip = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(6),
                Background = HexToBrush(p.Hex), BorderBrush = HexToBrush("#4A443C"), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, ToolTip = Loc.PresetDisplay(p), Tag = p,
            };
            chip.MouseLeftButtonUp += (_, _) => ApplyColorPreset(p);
            chip.MouseRightButtonUp += (_, _) => DeleteColorPreset(p);
            ColorChips.Children.Add(chip);
        }
        ColorChips.Children.Add(MakeAddTile(Loc.T("S_preset_add_color"), SaveColorPreset));
    }

    private void ApplyColorPreset(ColorPresetData p)
    {
        Logger.Info($"Apply color preset: {p.Name}");
        _suppressSend = true;
        SetColorUI(p.Hex);
        HueSlider.Value = p.Shift * 360.0;
        SatSlider.Value = p.Sat * 50.0;
        BriSlider.Value = p.Bri * 50.0;
        LiftSlider.Value = p.Lift * 100.0;
        _suppressSend = false;
        SyncAll();
    }

    private void SaveColorPreset()
    {
        var name = InputDialog.Ask(this, Loc.T("S_prompt_color_name"));
        if (name is null) return;
        _presets.Data.ColorPresets.Add(new ColorPresetData
        {
            Name = name, Hex = CurrentHexText.Text, Shift = HueParam, Sat = SatParam, Bri = BriParam, Lift = LiftParam,
        });
        _presets.Save();
        BuildColorChips();
    }

    private void DeleteColorPreset(ColorPresetData p)
    {
        if (!ConfirmDialog.Confirm(this, string.Format(Loc.T("S_confirm_delete_color"), Loc.PresetDisplay(p))))
        {
            return;
        }
        _presets.Data.ColorPresets.Remove(p);
        _presets.Save();
        BuildColorChips();
    }

    // ===== フルプリセット（ピル） =====

    private void BuildFullPresetChips()
    {
        FullPresetChips.Children.Clear();
        foreach (var p in _presets.Data.FullPresets)
        {
            var pill = MakePill(p.Name);
            pill.MouseLeftButtonUp += (_, _) => ApplyFullPreset(p);
            pill.MouseRightButtonUp += (_, _) => DeleteFullPreset(p);
            FullPresetChips.Children.Add(pill);
        }
        FullPresetChips.Children.Add(MakeAddPill(Loc.T("S_preset_add_full"), SaveFullPreset));
    }

    private void ApplyFullPreset(FullPresetData p)
    {
        Logger.Info($"Apply full preset: {p.Name}");
        _suppressSend = true;
        SetColorUI(p.Hex);
        HueSlider.Value = p.Shift * 360.0;
        SatSlider.Value = p.Sat * 50.0;
        BriSlider.Value = p.Bri * 50.0;
        LiftSlider.Value = p.Lift * 100.0;
        ThresholdSlider.Value = p.Threshold * 100.0;
        GuideSlider.Value = p.Guide * 100.0;
        TolSlider.Value = p.Tol * 100.0;
        SoftSlider.Value = p.Soft * 10.0;
        _suppressSend = false;
        SyncAll();
    }

    private void SaveFullPreset()
    {
        var name = InputDialog.Ask(this, Loc.T("S_prompt_full_name"));
        if (name is null) return;
        _presets.Data.FullPresets.Add(new FullPresetData
        {
            Name = name, Hex = CurrentHexText.Text, Shift = HueParam, Sat = SatParam, Bri = BriParam, Lift = LiftParam,
            Threshold = ThresholdParam, Guide = GuideParam, Tol = TolParam, Soft = SoftParam,
        });
        _presets.Save();
        BuildFullPresetChips();
    }

    private void DeleteFullPreset(FullPresetData p)
    {
        if (!ConfirmDialog.Confirm(this, string.Format(Loc.T("S_confirm_delete_full"), p.Name)))
        {
            return;
        }
        _presets.Data.FullPresets.Remove(p);
        _presets.Save();
        BuildFullPresetChips();
    }

    // ===== 共通UI部品 =====

    private Border MakeAddTile(string tooltip, Action onClick)
    {
        var tile = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent, BorderBrush = HexToBrush("#4A443C"), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, ToolTip = tooltip,
            Child = new TextBlock
            {
                Text = "＋", Foreground = HexToBrush("#A79F95"), FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        tile.MouseLeftButtonUp += (_, _) => onClick();
        return tile;
    }

    private Border MakePill(string text)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = HexToBrush("#211D18"), BorderBrush = HexToBrush("#322C25"), BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
            ToolTip = Loc.T("S_chip_tip"),
            Child = new TextBlock { Text = text, Foreground = HexToBrush("#F4F0E9"), FontSize = 13 },
        };
    }

    private Border MakeAddPill(string tooltip, Action onClick)
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent, BorderBrush = HexToBrush("#322C25"), BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = new TextBlock { Text = Loc.T("S_save_pill"), Foreground = HexToBrush("#A79F95"), FontSize = 13 },
        };
        pill.MouseLeftButtonUp += (_, _) => onClick();
        return pill;
    }

    private void SetColorUI(string hex)
    {
        CurrentSwatch.Background = HexToBrush(hex);
        CurrentHexText.Text = hex;
    }

    // 接続時やプリセット適用時、GUIの現在値をエンジンへ一括送信して状態を揃える
    private void SyncAll()
    {
        _engine.Send($"color {CurrentHexText.Text}");
        _engine.Send($"sat {SatParam.ToString("0.####", CultureInfo.InvariantCulture)}");
        _engine.Send($"bri {BriParam.ToString("0.####", CultureInfo.InvariantCulture)}");
        _engine.Send($"lift {LiftParam.ToString("0.####", CultureInfo.InvariantCulture)}");
        _engine.Send($"shift {HueParam.ToString("0.####", CultureInfo.InvariantCulture)}");
        _engine.Send($"threshold {ThresholdParam.ToString("0.####", CultureInfo.InvariantCulture)}");
        _engine.Send($"guide {GuideParam.ToString("0.####", CultureInfo.InvariantCulture)}");
        _engine.Send($"tol {TolParam.ToString("0.####", CultureInfo.InvariantCulture)}");
        _engine.Send($"soft {SoftParam.ToString("0.####", CultureInfo.InvariantCulture)}");
    }

    private void OnConnectionChanged(bool connected)
    {
        Dispatcher.Invoke(() =>
        {
            _connected = connected;
            StatusDot.Fill = HexToBrush(connected ? "#3E8E63" : "#7C766D");
            StatusText.Text = connected ? Loc.T("S_status_connected") : Loc.T("S_status_disconnected");
            if (connected)
            {
                SyncAll();
                Logger.Info("Synced all parameters to engine on connect");
            }
        });
    }

    // ===== 既定に戻す =====

    private const string DefaultColor = "#1A1512";

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Reset to defaults clicked");
        _suppressSend = true;
        SetColorUI(DefaultColor);
        SatSlider.Value = 0;
        BriSlider.Value = 0;
        LiftSlider.Value = 45;
        HueSlider.Value = 0;
        ThresholdSlider.Value = 26;
        GuideSlider.Value = 70;
        TolSlider.Value = 50;
        SoftSlider.Value = 15;
        // 値が既定と同じでValueChangedが出ないスライダーの表示も更新する
        SatValue.Text = "0.00"; BriValue.Text = "0.00"; LiftValue.Text = "0.45"; HueValue.Text = "0";
        ThresholdValue.Text = "0.26"; GuideValue.Text = "0.70"; TolValue.Text = "0.50"; SoftValue.Text = "1.5";
        _suppressSend = false;
        SyncAll();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Minimize clicked");
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Close clicked");
        // 実際の終了/トレイ格納は OnClosing で設定に従って分岐する
        Close();
    }

    private static SolidColorBrush HexToBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        return new SolidColorBrush(color);
    }
}
