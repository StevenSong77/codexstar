using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Drawing2D = System.Drawing.Drawing2D;
using IOPath = System.IO.Path;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using ShapePath = System.Windows.Shapes.Path;

namespace CodexStatusLight;

public partial class MainWindow : Window
{
    private const double CardWidth = 326;
    private const double CardHeight = 94;
    private const double CollapsedHeight = 48;
    private const double Gap = 10;
    private const double PanelPadding = 8;
    private const double PanelCornerRadius = 20;
    private const double ToggleButtonSize = 22;
    private const double ToggleButtonInset = 8;
    private const double CollapsedBulbSize = 18;
    private const double CollapsedBulbSpacing = 25;
    private const double CollapsedBulbLeftMargin = 18;
    private const double CollapsedBulbToToggleGap = 10;
    private const double QuotaRingSize = 62;
    private const double QuotaRingStroke = 5.6;
    private const double QuotaOnlyRingSize = 70;
    private const double QuotaOnlyRingStroke = 6.1;
    private const double QuotaOnlyPanelWidth = 260;
    private const double QuotaPanelWidth = 304;
    private const double MinUiScalePercent = 10.0;
    private const double MaxUiScalePercent = 500.0;
    private const int NormalAnimationFrameRate = 30;
    private const int SlideAnimationFrameRate = 144;
    private const int PixelRows = 8;
    private const int PixelCols = 30;
    private const int MaxVisibleTasks = 8;
    private const double AcknowledgementDebounceSeconds = 0.45;
    private const double ForegroundDoneGraceSeconds = 2.0;
    private const double DefaultCompletionSoundThresholdMinutes = 3.0;
    private const string DefaultCompletionSoundId = "dingdong";
    private const string ShortCompletionSoundRelativePath = @"Assets\Audio\codex-done-short.wav";
    private const int MaxSeenEvents = 5000;
    private const int SeenEventsTrimBatch = 500;
    private const int MaxCompletionSoundedTurns = 500;
    private const int CompletionSoundTrimBatch = 50;
    private const int CompletionSoundSettleDelayMilliseconds = 200;
    private const int MaxInactiveFileOffsets = 500;
    private const int MaxActiveCompletionPlayers = 4;
    private const int CompletionPlayerTimeoutSeconds = 30;
    private const int ExternalBalanceRefreshSeconds = 30;
    private const string ExternalBalancesFileName = "external-balances.json";

    private static readonly HttpClient ExternalBalanceHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    private static readonly CompletionSoundChoice[] CompletionSoundChoices =
    {
        new(DefaultCompletionSoundId, "叮咚", "Ding Dong", ShortCompletionSoundRelativePath),
        new("Switch.mp3", "Switch", "Switch", @"Assets\Audio\Choices\Switch.mp3"),
        new("光束.mp3", "光束", "Light Beam", @"Assets\Audio\Choices\光束.mp3"),
        new("塞尔达.mp3", "塞尔达", "Zelda", @"Assets\Audio\Choices\塞尔达.mp3"),
        new("成就系统.mp3", "成就系统", "Achievement", @"Assets\Audio\Choices\成就系统.mp3"),
        new("精灵尘.mp3", "精灵尘", "Fairy Dust", @"Assets\Audio\Choices\精灵尘.mp3"),
        new("闪讯.mp3", "闪讯", "Flash Ping", @"Assets\Audio\Choices\闪讯.mp3"),
        new("马里奥.mp3", "马里奥", "Mario", @"Assets\Audio\Choices\马里奥.mp3"),
    };

    private static readonly Regex ThreadIdRegex = new(
        @"rollout-.+-(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\.jsonl$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<string, TaskState> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCard> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DispatcherTimer> _exitTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _activeTurnsByFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _exitingCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _fileOffsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DispatcherTimer> _pendingCompletionSoundTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingCompletionSoundThreadIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _threadTitles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GoalState> _goals = new(StringComparer.OrdinalIgnoreCase);
    private RateLimitSnapshot? _rateLimits;
    private readonly HashSet<string> _seenEvents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _seenEventOrder = new();
    private readonly HashSet<string> _unreadThreadIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _paintTimer;
    private readonly DispatcherTimer _globalStateDebounceTimer;
    private readonly DispatcherTimer _externalBalanceTimer;
    private readonly string _codexRoot;
    private readonly string _sessionsRoot;
    private readonly string _stateDir;
    private readonly string _debugLogPath;
    private readonly string _manualStatePath;
    private readonly string _settingsPath;
    private readonly string _externalBalancesPath;
    private readonly string _globalStatePath;
    private readonly string _sessionIndexPath;
    private readonly string _goalsDbPath;
    private FileSystemWatcher? _sessionWatcher;
    private FileSystemWatcher? _stateWatcher;
    private FileSystemWatcher? _globalWatcher;
    private DateTime _lastManualWriteUtc = DateTime.MinValue;
    private DateTime _lastSessionIndexWriteUtc = DateTime.MinValue;
    private DateTime _lastGlobalStateWriteUtc = DateTime.MinValue;
    private DateTime _lastGoalsDbWriteUtc = DateTime.MinValue;
    private DateTime _lastGoalsWalWriteUtc = DateTime.MinValue;
    private DateTime _lastSessionWatcherEventUtc = DateTime.MinValue;
    private long _lastSessionIndexLength = -1;
    private long _lastGlobalStateLength = -1;
    private long _lastGoalsDbLength = -1;
    private long _lastGoalsWalLength = -1;
    private DateTime _lastActiveFilePollUtc = DateTime.MinValue;
    private DateTime _lastRecentFileScanUtc = DateTime.MinValue;
    private int _tick;
    private bool _isCollapsed;
    private bool _isQuotaPinned;
    private bool _showQuotaPercentInRing = true;
    private bool _showExternalBalances = true;
    private int _externalBalanceProviderCount = 2;
    private bool _dingDongEnabled = true;
    private string _completionSoundChoiceId = DefaultCompletionSoundId;
    private double _completionSoundThresholdMinutes = DefaultCompletionSoundThresholdMinutes;
    private UiLanguage _uiLanguage = UiLanguage.Chinese;
    private string _shengshengBalanceText = "--";
    private string _shengshengBalanceUpdatedAtText = "";
    private string _deepkeyBalanceText = "--";
    private double? _shengshengBalanceAmount;
    private double? _deepkeyBalanceAmount;
    private string _shengshengBalancePrefix = "";
    private string _deepkeyBalancePrefix = "";
    private double? _shengshengBaselineAmount;
    private double? _deepkeyBaselineAmount;
    private double? _shengshengLastObservedAmount;
    private double? _deepkeyLastObservedAmount;
    private double? _shengshengConsumedAmount;
    private double? _deepkeyConsumedAmount;
    private bool _externalBalanceRefreshInFlight;
    private bool _suppressCompletionSound;
    private bool _suppressSessionReplayDebug;
    private bool _isBootstrappingSessions;
    private readonly DateTime _runtimeStartedAtUtc = DateTime.UtcNow;
    private double _uiScalePercent = 100.0;
    private bool _isModeTransitionRunning;
    private DispatcherTimer? _modeTransitionTimer;
    private Border? _collapsedStrip;
    private string? _collapsedSignature;
    private string? _expandedRenderSignature;
    private double _collapsedWidth = double.NaN;
    private Popup? _windowContextMenu;
    private Window? _trayContextMenu;
    private IntPtr _trayMouseHook = IntPtr.Zero;
    private LowLevelMouseProc? _trayMouseHookProc;
    private Forms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayStatusIcon;
    private readonly HashSet<string> _completionSoundedTurns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _completionSoundedTurnOrder = new();
    private readonly List<MediaPlayer> _activeCompletionPlayers = new();
    private readonly Dictionary<MediaPlayer, DispatcherTimer> _completionPlayerTimeouts = new();
    private readonly bool _verboseLogging = ReadBooleanEnvironment("CODEX_STATUS_LIGHT_VERBOSE_LOGGING") ||
                                            ReadBooleanEnvironment("CODEX_STATUS_LIGHT_DEBUG");
    private readonly Dictionary<string, DateTime> _lastDebugLogByEvent = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastDebugRotationCheckUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        Root.CacheMode = null;
        RenderOptions.SetBitmapScalingMode(Root, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(Root, EdgeMode.Unspecified);
        TextOptions.SetTextFormattingMode(Root, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(Root, TextRenderingMode.Auto);
        TextOptions.SetTextHintingMode(Root, TextHintingMode.Fixed);

        _codexRoot = Environment.GetEnvironmentVariable("CODEX_STATUS_LIGHT_CODEX_ROOT") ??
                     IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        _sessionsRoot = IOPath.Combine(_codexRoot, "sessions");
        _globalStatePath = IOPath.Combine(_codexRoot, ".codex-global-state.json");
        _sessionIndexPath = IOPath.Combine(_codexRoot, "session_index.jsonl");
        _goalsDbPath = IOPath.Combine(_codexRoot, "goals_1.sqlite");
        var configuredStateDir = Environment.GetEnvironmentVariable("CODEX_STATUS_LIGHT_STATE_DIR");
        _stateDir = string.IsNullOrWhiteSpace(configuredStateDir)
            ? GetDefaultStateDir()
            : configuredStateDir;
        _manualStatePath = IOPath.Combine(_stateDir, "state.json");
        _settingsPath = IOPath.Combine(_stateDir, "settings.json");
        _externalBalancesPath = IOPath.Combine(_stateDir, ExternalBalancesFileName);
        _debugLogPath = IOPath.Combine(_stateDir, "debug.jsonl");
        Directory.CreateDirectory(_stateDir);
        MigrateLegacySettingsIfNeeded(string.IsNullOrWhiteSpace(configuredStateDir));
        LoadUiSettings();
        ApplyUiScaleTransform();
        DebugLog("startup", new { codexRoot = _codexRoot, sessionsRoot = _sessionsRoot });

        ConfigureWatchers();
        LoadThreadTitles(force: true);
        LoadUnreadThreadIds(force: true);
        LoadGoals(force: true);
        BootstrapRecentSessions();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pollTimer.Tick += (_, _) => Poll();
        _pollTimer.Start();

        _paintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _paintTimer.Tick += (_, _) => AnimatePixels();
        _paintTimer.Start();

        _globalStateDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _globalStateDebounceTimer.Tick += (_, _) =>
        {
            _globalStateDebounceTimer.Stop();
            RefreshGlobalStateFromWatcher();
        };

        _externalBalanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ExternalBalanceRefreshSeconds) };
        _externalBalanceTimer.Tick += async (_, _) => await RefreshExternalBalancesAsync();
        _externalBalanceTimer.Start();
        _ = RefreshExternalBalancesAsync();

        Loaded += (_, _) =>
        {
            PositionBottomRight();
            Render();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                AcknowledgeAllCompleted();
                return;
            }

            DragMove();
        };

        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowWindowContextMenu();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _windowContextMenu is not null)
            {
                CloseWindowContextMenu();
                e.Handled = true;
            }
        };

        ConfigureTrayIcon();
        Closed += (_, _) => DisposeRuntimeResources();
    }

    private void ShowWindowContextMenu()
    {
        CloseWindowContextMenu();

        var scale = UiScale;
        var popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.MousePoint,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
        };

        var stack = CreateContextMenuStack(scale, CloseWindowContextMenu, includeShow: false);
        var shell = CreateContextMenuShell(stack, scale);
        shell.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseWindowContextMenu();
                e.Handled = true;
            }
        };

        popup.Child = shell;
        popup.Opened += (_, _) => shell.Focus();
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_windowContextMenu, popup))
            {
                _windowContextMenu = null;
            }
        };
        _windowContextMenu = popup;
        popup.IsOpen = true;
    }

    private void CloseWindowContextMenu()
    {
        if (_windowContextMenu is null)
        {
            return;
        }

        _windowContextMenu.IsOpen = false;
        _windowContextMenu = null;
    }

    private bool IsEnglishUi => _uiLanguage == UiLanguage.English;

    private string Ui(string zh, string en)
    {
        return IsEnglishUi ? en : zh;
    }

    private string LanguageToggleText()
    {
        return IsEnglishUi ? "中文 UI" : "English UI";
    }

    private string GetCompletionSoundDisplayName(CompletionSoundChoice choice)
    {
        return IsEnglishUi ? choice.DisplayNameEn : choice.DisplayNameZh;
    }

    private void ToggleUiLanguage()
    {
        _uiLanguage = IsEnglishUi ? UiLanguage.Chinese : UiLanguage.English;
        _collapsedSignature = null;
        _expandedRenderSignature = null;
        _collapsedWidth = double.NaN;
        SaveUiSettings();

        foreach (var card in _cards.Values.Distinct())
        {
            UpdateQuotaPinVisual(card);
        }

        DebugLog("ui_language_toggle", new { language = IsEnglishUi ? "en" : "zh" });
        Render();
    }

    private void ShowSettingsDialog()
    {
        var dialog = CreateGlassDialog();
        var stack = new StackPanel { MinWidth = ScaleValue(236) };

        void Open(Action action)
        {
            dialog.Close();
            Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }

        stack.Children.Add(CreateDialogHeading(Ui("设置", "Settings")));
        stack.Children.Add(CreateDialogActionRow(
            _isQuotaPinned ? Ui("松开额度面", "Unpin Quota") : Ui("固定额度面", "Pin Quota"),
            () => Open(PinQuotaPageFromMenu)));
        stack.Children.Add(CreateDialogActionRow(
            _showQuotaPercentInRing ? Ui("显示更新时间", "Show Reset Time") : Ui("显示百分比", "Show Percent"),
            () => Open(ToggleQuotaDisplayMode)));
        stack.Children.Add(CreateDialogActionRow(Ui("额度页面布置", "Quota Layout"), () => Open(ShowQuotaLayoutDialog)));
        stack.Children.Add(CreateDialogActionRow(LanguageToggleText(), () => Open(ToggleUiLanguage)));
        stack.Children.Add(CreateDialogActionRow(Ui("提示音个性化", "Sound Personalization"), () => Open(ShowCompletionSoundDialog)));

        dialog.Content = CreateGlassDialogShell(stack);
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };
        dialog.ShowDialog();
    }

    private void ShowQuotaLayoutDialog()
    {
        var dialog = CreateGlassDialog();
        var credentials = ReadExternalBalanceEditorValues();
        var showBalancesDraft = _showExternalBalances;
        var providerCountDraft = _externalBalanceProviderCount;

        Border ChoiceButton(string text, bool selected, bool enabled, Action onClick)
        {
            var marker = CreateSegmentRadioMarker(selected);
            var label = new TextBlock
            {
                Text = text,
                FontFamily = FontForChinese(),
                FontWeight = FontWeights.SemiBold,
                FontSize = ScaleValue(12.7),
                Foreground = CreateFrozenBrush(enabled ? Color.FromRgb(248, 250, 255) : Color.FromRgb(126, 134, 150)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(marker);
            content.Children.Add(label);
            var button = new Border
            {
                Height = ScaleValue(31),
                MinWidth = ScaleValue(66),
                Padding = ScaleThickness(0, 0, 10, 0),
                Margin = ScaleThickness(0, 0, 5, 0),
                Background = Brushes.Transparent,
                Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
                Opacity = enabled ? 1 : 0.36,
                IsEnabled = enabled,
                Child = content,
            };
            button.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onClick();
            };
            return button;
        }

        TextBox TextInput(string text)
        {
            return new TextBox
            {
                Text = text,
                FontFamily = FontForDuration(),
                FontWeight = FontWeights.SemiBold,
                FontSize = ScaleValue(12.8),
                Foreground = CreateFrozenBrush(Color.FromRgb(224, 232, 246)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = ScaleThickness(0, 0, 0, 1),
                CaretBrush = CreateFrozenBrush(Color.FromRgb(248, 251, 255)),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
        }

        PasswordBox TokenInput(string value)
        {
            return new PasswordBox
            {
                Password = value,
                FontFamily = FontForDuration(),
                FontWeight = FontWeights.SemiBold,
                FontSize = ScaleValue(12.8),
                Foreground = CreateFrozenBrush(Color.FromRgb(224, 232, 246)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = ScaleThickness(0, 0, 0, 1),
                CaretBrush = CreateFrozenBrush(Color.FromRgb(248, 251, 255)),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
        }

        Border Field(string label, Control input)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = FontForChinese(),
                FontWeight = FontWeights.Medium,
                FontSize = ScaleValue(11.2),
                Foreground = CreateFrozenBrush(Color.FromRgb(186, 196, 215)),
                Margin = ScaleThickness(0, 4, 0, 3),
            });
            stack.Children.Add(new Border
            {
                Height = ScaleValue(32),
                CornerRadius = ScaleCornerRadius(10),
                Padding = ScaleThickness(9, 1, 9, 1),
                Background = CreateDialogFieldBrush(),
                BorderBrush = CreateFrozenBrush(Color.FromArgb(82, 206, 216, 236)),
                BorderThickness = new Thickness(ScaleValue(1)),
                Child = input,
            });
            return new Border { Child = stack };
        }

        Border Station(string title, TextBox displayName, PasswordBox token)
        {
            var station = new StackPanel();
            station.Children.Add(CreateDialogHeading(title));
            station.Children.Add(Field(Ui("中转站名称", "Provider Name"), displayName));
            station.Children.Add(Field(Ui("访问令牌", "Access Token"), token));
            return new Border
            {
                Margin = ScaleThickness(0, 5, 0, 3),
                Padding = ScaleThickness(9, 5, 9, 7),
                CornerRadius = ScaleCornerRadius(12),
                Background = CreateFrozenBrush(Color.FromArgb(22, 210, 220, 240)),
                BorderBrush = CreateFrozenBrush(Color.FromArgb(52, 205, 216, 235)),
                BorderThickness = new Thickness(ScaleValue(1)),
                Child = station,
            };
        }

        var showRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var countRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var shengshengToken = TokenInput(credentials.ShengshengToken);
        var deepkeyToken = TokenInput(credentials.DeepkeyToken);
        var shengshengName = TextInput(credentials.ShengshengDisplayName);
        var deepkeyName = TextInput(credentials.DeepkeyDisplayName);
        var firstStation = Station(Ui("站点 1", "Provider 1"), shengshengName, shengshengToken);
        var secondStation = Station(Ui("站点 2", "Provider 2"), deepkeyName, deepkeyToken);
        var providerArea = new StackPanel();
        providerArea.Children.Add(CreateDialogHeading(Ui("中转站数量", "Provider Count")));
        providerArea.Children.Add(countRow);
        providerArea.Children.Add(firstStation);
        providerArea.Children.Add(secondStation);

        void RefreshChoices()
        {
            showRow.Children.Clear();
            showRow.Children.Add(ChoiceButton(Ui("不显示", "Hide"), !showBalancesDraft, true, () =>
            {
                showBalancesDraft = false;
                RefreshChoices();
            }));
            showRow.Children.Add(ChoiceButton(Ui("显示", "Show"), showBalancesDraft, true, () =>
            {
                showBalancesDraft = true;
                RefreshChoices();
            }));

            countRow.Children.Clear();
            countRow.Children.Add(ChoiceButton(Ui("一个", "One"), providerCountDraft == 1, showBalancesDraft, () =>
            {
                providerCountDraft = 1;
                RefreshChoices();
            }));
            countRow.Children.Add(ChoiceButton(Ui("两个", "Two"), providerCountDraft == 2, showBalancesDraft, () =>
            {
                providerCountDraft = 2;
                RefreshChoices();
            }));

            providerArea.Visibility = showBalancesDraft ? Visibility.Visible : Visibility.Collapsed;
            secondStation.IsEnabled = showBalancesDraft && providerCountDraft == 2;
            secondStation.Opacity = secondStation.IsEnabled ? 1 : 0.34;
        }

        void CloseDialog(bool apply)
        {
            if (apply)
            {
                SaveExternalBalanceLayoutConfiguration(
                    showBalancesDraft,
                    providerCountDraft,
                    shengshengName.Text,
                    credentials.ShengshengUserId,
                    shengshengToken.Password,
                    deepkeyName.Text,
                    credentials.DeepkeyUserId,
                    deepkeyToken.Password);
            }

            dialog.Close();
        }

        var stack = new StackPanel { MinWidth = ScaleValue(270), MaxWidth = ScaleValue(310) };
        stack.Children.Add(CreateDialogHeading(Ui("额度页面布置", "Quota Layout")));
        stack.Children.Add(new TextBlock
        {
            Text = Ui("中转站余额", "Provider Balances"),
            FontFamily = FontForChinese(),
            FontWeight = FontWeights.Medium,
            FontSize = ScaleValue(11.2),
            Foreground = CreateFrozenBrush(Color.FromRgb(186, 196, 215)),
            Margin = ScaleThickness(0, 2, 0, 3),
        });
        stack.Children.Add(showRow);
        stack.Children.Add(providerArea);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = ScaleThickness(0, 12, 0, 0),
        };
        buttons.Children.Add(CreateScaleDialogButton(Ui("取消", "Cancel"), () => CloseDialog(false)));
        buttons.Children.Add(CreateScaleDialogButton(Ui("确定", "OK"), () => CloseDialog(true)));
        stack.Children.Add(buttons);

        dialog.Content = CreateGlassDialogShell(stack);
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CloseDialog(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseDialog(false);
                e.Handled = true;
            }
        };
        dialog.Loaded += (_, _) =>
        {
            RefreshChoices();
            if (showBalancesDraft)
            {
                shengshengName.Focus();
            }
        };
        dialog.ShowDialog();
    }

    private StackPanel CreateContextMenuStack(double scale, Action closeMenu, bool includeShow)
    {
        var stack = new StackPanel
        {
            MinWidth = ScaleValue(86, scale),
        };

        void AddItem(string text, Action action)
        {
            stack.Children.Add(CreateContextMenuRow(text, () =>
            {
                closeMenu();
                action();
            }, scale));
        }

        void AddFooter(string text)
        {
            stack.Children.Add(new Border
            {
                Margin = ScaleThickness(6, 5, 6, 1, scale),
                Padding = ScaleThickness(8, 5, 8, 4, scale),
                CornerRadius = ScaleCornerRadius(8, scale),
                Background = CreateFrozenBrush(Color.FromArgb(14, 255, 255, 255)),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = CreateFrozenBrush(Color.FromArgb(168, 216, 224, 242)),
                    FontFamily = FontForDuration(),
                    FontSize = 12.2 * scale,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                },
            });
        }

        if (includeShow)
        {
            AddItem(IsVisible ? Ui("隐藏", "Hide") : Ui("显示", "Show"), IsVisible ? HideToTray : ShowFromTray);
            AddItem(LanguageToggleText(), ToggleUiLanguage);
            AddItem(Ui("缩放", "Scale"), ShowScaleDialog);
            AddItem(Ui("关闭", "Close"), Close);
            AddFooter("Codexstar v1.1");
            return stack;
        }

        AddItem(Ui("设置", "Settings"), ShowSettingsDialog);
        AddItem(Ui("缩放", "Scale"), ShowScaleDialog);
        AddItem(Ui("隐藏", "Hide"), HideToTray);
        AddItem(Ui("刷新", "Refresh"), HardRefreshStatusLight);

        stack.Children.Add(new Border
        {
            Height = ScaleValue(1, scale),
            Margin = ScaleThickness(10, 5, 10, 5, scale),
            Background = CreateFrozenBrush(Color.FromArgb(34, 219, 226, 242)),
            IsHitTestVisible = false,
        });
        AddItem(Ui("关闭", "Close"), Close);
        return stack;
    }

    private static Border CreateContextMenuShell(StackPanel stack, double scale)
    {
        var corner = 14 * scale;
        var borderThickness = Math.Max(1, 1 * scale);
        var shell = new Border
        {
            Focusable = true,
            SnapsToDevicePixels = true,
            Child = new Grid
            {
                Children =
                {
                    new Border
                    {
                        CornerRadius = new CornerRadius(corner),
                        Padding = ScaleThickness(6, 6, 6, 6, scale),
                        Background = CreateContextMenuBrush(),
                        BorderBrush = CreateFrozenBrush(Color.FromArgb(92, 205, 214, 235)),
                        BorderThickness = new Thickness(borderThickness),
                        IsHitTestVisible = false,
                        Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            Color = Color.FromRgb(4, 8, 18),
                            BlurRadius = 18 * scale,
                            ShadowDepth = 7 * scale,
                            Opacity = 0.30,
                        },
                    },
                    new Border
                    {
                        CornerRadius = new CornerRadius(corner),
                        Padding = ScaleThickness(6, 6, 6, 6, scale),
                        Background = CreateContextMenuBrush(),
                        BorderBrush = CreateFrozenBrush(Color.FromArgb(92, 205, 214, 235)),
                        BorderThickness = new Thickness(borderThickness),
                        SnapsToDevicePixels = true,
                        Child = stack,
                    },
                },
            },
        };
        TextOptions.SetTextFormattingMode(shell, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(shell, TextRenderingMode.Auto);
        TextOptions.SetTextHintingMode(shell, TextHintingMode.Fixed);
        return shell;
    }

    private Window CreateGlassDialog()
    {
        var dialog = new Window
        {
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SnapsToDevicePixels = true,
        };
        EnableDialogDrag(dialog);
        return dialog;
    }

    private static void EnableDialogDrag(Window dialog)
    {
        bool IsInteractive(DependencyObject? source)
        {
            var current = source;
            while (current is not null && !ReferenceEquals(current, dialog))
            {
                if (current is Control || current is Border { Cursor: not null })
                {
                    return true;
                }

                DependencyObject? parent = null;
                try
                {
                    parent = VisualTreeHelper.GetParent(current);
                }
                catch
                {
                }
                current = parent ?? LogicalTreeHelper.GetParent(current);
            }

            return false;
        }

        dialog.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || IsInteractive(e.OriginalSource as DependencyObject))
            {
                return;
            }

            try
            {
                dialog.DragMove();
                e.Handled = true;
            }
            catch
            {
            }
        };
    }

    private Border CreateDialogHeading(string text)
    {
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = ScaleCornerRadius(7),
            Padding = ScaleThickness(9, 4, 9, 4),
            Margin = ScaleThickness(0, 0, 0, 7),
            Background = CreateDialogHeadingPlateBrush(),
            BorderBrush = CreateFrozenBrush(Color.FromArgb(34, 238, 242, 250)),
            BorderThickness = new Thickness(ScaleValue(1)),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = FontForChinese(),
                FontWeight = FontWeights.SemiBold,
                FontSize = ScaleValue(12.2),
                Foreground = CreateFrozenBrush(Color.FromRgb(248, 250, 255)),
            },
        };
    }

    private Border CreateDialogActionRow(string text, Action action)
    {
        var row = CreateContextMenuRow(text, action);
        row.MinWidth = ScaleValue(226);
        row.Margin = ScaleThickness(0, 2, 0, 2);
        return row;
    }

    private Grid CreateGlassDialogShell(UIElement content)
    {
        var padding = ScaleThickness(16, 14, 16, 14);
        var corner = ScaleCornerRadius(16);
        var rim = CreateFrozenBrush(Color.FromArgb(104, 205, 214, 235));
        return new Grid
        {
            Children =
            {
                new Border
                {
                    CornerRadius = corner,
                    Padding = padding,
                    Background = CreateContextMenuBrush(),
                    BorderBrush = rim,
                    BorderThickness = new Thickness(ScaleValue(1)),
                    IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(4, 8, 18),
                        BlurRadius = ScaleValue(22),
                        ShadowDepth = ScaleValue(8),
                        Opacity = 0.34,
                    },
                },
                new Border
                {
                    CornerRadius = corner,
                    Padding = padding,
                    Background = CreateContextMenuBrush(),
                    BorderBrush = rim,
                    BorderThickness = new Thickness(ScaleValue(1)),
                    Child = content,
                },
            },
        };
    }

    private void ShowScaleDialog()
    {
        var dialog = new Window
        {
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SnapsToDevicePixels = true,
        };
        EnableDialogDrag(dialog);

        var input = new TextBox
        {
            Text = Math.Round(_uiScalePercent).ToString("0", CultureInfo.InvariantCulture),
            FontFamily = FontForDuration(),
            FontWeight = FontWeights.Black,
            FontSize = ScaleValue(22),
            Foreground = CreateFrozenBrush(Color.FromRgb(236, 242, 252)),
            Background = CreateFrozenBrush(Color.FromArgb(118, 10, 13, 20)),
            BorderBrush = CreateFrozenBrush(Color.FromArgb(92, 214, 222, 242)),
            BorderThickness = new Thickness(ScaleValue(1)),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = ScaleValue(36),
            Margin = ScaleThickness(0, 9, 0, 9),
            CaretBrush = CreateFrozenBrush(Color.FromRgb(236, 242, 252)),
        };

        var hint = new TextBlock
        {
            Text = "10% - 500%",
            FontFamily = FontForChinese(),
            FontSize = ScaleValue(11.5),
            FontWeight = FontWeights.Medium,
            Foreground = CreateFrozenBrush(Color.FromRgb(160, 169, 188)),
            TextAlignment = TextAlignment.Center,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = ScaleThickness(0, 9, 0, 0),
        };

        void CloseDialog(bool apply)
        {
            if (apply && TryParseScalePercent(input.Text, out var percent))
            {
                SetUiScalePercent(percent);
            }

            dialog.Close();
        }

        buttons.Children.Add(CreateScaleDialogButton(Ui("取消", "Cancel"), () => CloseDialog(false)));
        buttons.Children.Add(CreateScaleDialogButton(Ui("确定", "OK"), () => CloseDialog(true)));

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Ui("缩放", "Scale"),
            FontFamily = FontForChinese(),
            FontWeight = FontWeights.SemiBold,
            FontSize = ScaleValue(14),
            Foreground = CreateFrozenBrush(Color.FromRgb(229, 235, 246)),
            TextAlignment = TextAlignment.Center,
        });
        stack.Children.Add(input);
        stack.Children.Add(hint);
        stack.Children.Add(buttons);

        var shell = new Grid
        {
            Children =
            {
                new Border
                {
                    CornerRadius = ScaleCornerRadius(16),
                    Padding = ScaleThickness(16, 14, 16, 14),
                    Background = CreateContextMenuBrush(),
                    BorderBrush = CreateFrozenBrush(Color.FromArgb(104, 205, 214, 235)),
                    BorderThickness = new Thickness(ScaleValue(1)),
                    IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(4, 8, 18),
                        BlurRadius = ScaleValue(22),
                        ShadowDepth = ScaleValue(8),
                        Opacity = 0.34,
                    },
                },
                new Border
                {
                    CornerRadius = ScaleCornerRadius(16),
                    Padding = ScaleThickness(16, 14, 16, 14),
                    Background = CreateContextMenuBrush(),
                    BorderBrush = CreateFrozenBrush(Color.FromArgb(104, 205, 214, 235)),
                    BorderThickness = new Thickness(ScaleValue(1)),
                    Child = stack,
                },
            },
        };
        TextOptions.SetTextFormattingMode(shell, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(shell, TextRenderingMode.Auto);
        TextOptions.SetTextHintingMode(shell, TextHintingMode.Fixed);
        dialog.Content = shell;

        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CloseDialog(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseDialog(false);
                e.Handled = true;
            }
        };
        dialog.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        dialog.ShowDialog();
    }

    private void ShowCompletionSoundDialog()
    {
        var dialog = new Window
        {
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SnapsToDevicePixels = true,
        };
        EnableDialogDrag(dialog);

        Border LabelPlate(string text)
        {
            return new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = ScaleCornerRadius(7),
                Padding = ScaleThickness(9, 4, 9, 4),
                Margin = ScaleThickness(0, 10, 0, 6),
                Background = CreateDialogHeadingPlateBrush(),
                BorderBrush = CreateFrozenBrush(Color.FromArgb(34, 238, 242, 250)),
                BorderThickness = new Thickness(ScaleValue(1)),
                Child = new TextBlock
                {
                    Text = text,
                    FontFamily = FontForChinese(),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = ScaleValue(12.2),
                    Foreground = CreateFrozenBrush(Color.FromRgb(248, 250, 255)),
                    TextAlignment = TextAlignment.Left,
                },
            };
        }

        Border SegmentButton(string text, bool selected, Action onClick)
        {
            var marker = CreateSegmentRadioMarker(selected);
            var label = new TextBlock
            {
                Text = text,
                FontFamily = FontForChinese(),
                FontWeight = FontWeights.SemiBold,
                FontSize = ScaleValue(13.2),
                Foreground = CreateFrozenBrush(Color.FromRgb(248, 250, 255)),
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(marker);
            content.Children.Add(label);
            var button = new Border
            {
                MinWidth = ScaleValue(66),
                Height = ScaleValue(32),
                CornerRadius = ScaleCornerRadius(11),
                Padding = ScaleThickness(0, 0, 10, 0),
                Margin = ScaleThickness(0, 0, 4, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Child = content,
                Tag = selected,
            };
            button.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onClick();
            };
            return button;
        }

        var enabledDraft = _dingDongEnabled;
        Border? offOption = null;
        Border? onOption = null;
        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = ScaleThickness(0, 0, 0, 2),
        };

        void RefreshSegmentButtons()
        {
            modeRow.Children.Clear();
            offOption = SegmentButton(Ui("关闭", "Off"), !enabledDraft, () =>
            {
                enabledDraft = false;
                RefreshSegmentButtons();
                UpdateGate();
            });
            onOption = SegmentButton(Ui("启动", "On"), enabledDraft, () =>
            {
                enabledDraft = true;
                RefreshSegmentButtons();
                UpdateGate();
            });
            modeRow.Children.Add(offOption);
            modeRow.Children.Add(onOption);
        }

        var selectedChoice = GetCompletionSoundChoice(_completionSoundChoiceId);
        var selectedChoiceDraft = selectedChoice;
        var soundSelectorWidth = ScaleValue(132);
        var soundPopupContentWidth = ScaleValue(118);
        var selectedSoundText = new TextBlock
        {
            Text = GetCompletionSoundDisplayName(selectedChoiceDraft),
            FontFamily = FontForChinese(),
            FontWeight = FontWeights.SemiBold,
            FontSize = ScaleValue(13.0),
            Foreground = CreateFrozenBrush(Color.FromRgb(204, 211, 226)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = ScaleThickness(0, 0, 2, 0),
        };
        var chevron = new ShapePath
        {
            Data = Geometry.Parse("M 2 4.2 L 8 10.2 L 14 4.2"),
            Stroke = CreateFrozenBrush(Color.FromRgb(214, 220, 234)),
            StrokeThickness = ScaleValue(1.85),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = ScaleValue(16),
            Height = ScaleValue(14),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var chevronHost = new Border
        {
            Width = ScaleValue(27),
            Height = ScaleValue(27),
            CornerRadius = ScaleCornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = CreateSegmentIdleBrush(),
            BorderBrush = CreateFrozenBrush(Color.FromArgb(48, 232, 238, 250)),
            BorderThickness = new Thickness(ScaleValue(1)),
            Child = chevron,
        };
        Grid.SetColumn(selectedSoundText, 0);
        Grid.SetColumn(chevronHost, 1);
        var soundSelectorGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(28 * UiScale) },
            },
        };
        soundSelectorGrid.Children.Add(selectedSoundText);
        soundSelectorGrid.Children.Add(chevronHost);

        var soundSelector = new Border
        {
            Width = soundSelectorWidth,
            Height = ScaleValue(36),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = ScaleCornerRadius(11),
            Background = CreateDialogFieldBrush(),
            BorderBrush = CreateFrozenBrush(Color.FromArgb(82, 206, 216, 236)),
            BorderThickness = new Thickness(ScaleValue(1)),
            Padding = ScaleThickness(11, 0, 10, 0),
            Cursor = Cursors.Hand,
            Child = soundSelectorGrid,
        };

        var soundPopupStack = new StackPanel { Width = soundPopupContentWidth };
        var soundPopup = new Popup
        {
            PlacementTarget = soundSelector,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = 0,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = CreateContextMenuShell(soundPopupStack, UiScale),
        };
        Border SoundChoiceRow(CompletionSoundChoice choice, bool isSelected)
        {
            var rowIdleBrush = CreateFrozenBrush(Color.FromArgb(0, 255, 255, 255));
            var hoverBrush = CreateContextMenuRowHoverBrush();
            var text = new TextBlock
            {
                Text = GetCompletionSoundDisplayName(choice),
                Foreground = CreateFrozenBrush(isSelected ? Color.FromRgb(250, 252, 255) : Color.FromRgb(204, 211, 226)),
                FontFamily = FontForChinese(),
                FontSize = ScaleValue(13.4),
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium,
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = ScaleThickness(1, 0, 4, 0),
            };
            var previewButton = CreateSoundPreviewButton(choice);
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };
            Grid.SetColumn(text, 0);
            Grid.SetColumn(previewButton, 1);
            grid.Children.Add(text);
            grid.Children.Add(previewButton);

            var row = new Border
            {
                Width = soundPopupContentWidth,
                CornerRadius = ScaleCornerRadius(10),
                Padding = ScaleThickness(10, 4.5, 6, 4.5),
                Margin = ScaleThickness(0, 1, 0, 1),
                Background = isSelected ? CreateSegmentSelectedBrush() : rowIdleBrush,
                Cursor = Cursors.Hand,
                Child = grid,
            };
            row.MouseEnter += (_, _) => row.Background = isSelected ? CreateSegmentSelectedBrush() : hoverBrush;
            row.MouseLeave += (_, _) => row.Background = isSelected ? CreateSegmentSelectedBrush() : rowIdleBrush;
            row.MouseLeftButtonDown += (_, e) => e.Handled = true;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                selectedChoiceDraft = choice;
                selectedSoundText.Text = GetCompletionSoundDisplayName(choice);
                soundPopup.IsOpen = false;
            };
            return row;
        }

        void RebuildSoundPopupRows()
        {
            soundPopupStack.Children.Clear();
            foreach (var choice in CompletionSoundChoices)
            {
                var isSelected = string.Equals(choice.Id, selectedChoiceDraft.Id, StringComparison.OrdinalIgnoreCase);
                soundPopupStack.Children.Add(SoundChoiceRow(choice, isSelected));
            }
        }
        soundSelector.MouseLeftButtonDown += (_, e) =>
        {
            if (soundSelector.IsEnabled)
            {
                e.Handled = true;
            }
        };
        soundSelector.MouseLeftButtonUp += (_, e) =>
        {
            if (!soundSelector.IsEnabled)
            {
                return;
            }

            e.Handled = true;
            if (soundPopup.IsOpen)
            {
                soundPopup.IsOpen = false;
                return;
            }

            RebuildSoundPopupRows();
            soundPopup.IsOpen = true;
        };

        var thresholdInput = new TextBox
        {
            Text = _completionSoundThresholdMinutes.ToString("0.##", CultureInfo.InvariantCulture),
            FontFamily = FontForDuration(),
            FontWeight = FontWeights.Black,
            FontSize = ScaleValue(16),
            Foreground = CreateFrozenBrush(Color.FromRgb(204, 211, 226)),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = ScaleValue(44),
            Height = ScaleValue(26),
            Padding = ScaleThickness(0, 0, 0, 1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = CreateFrozenBrush(Color.FromRgb(236, 242, 252)),
        };
        var thresholdInputShell = new Border
        {
            Width = ScaleValue(54),
            Height = ScaleValue(34),
            CornerRadius = ScaleCornerRadius(10),
            Background = CreateDialogFieldBrush(),
            BorderBrush = CreateFrozenBrush(Color.FromArgb(82, 206, 216, 236)),
            BorderThickness = new Thickness(ScaleValue(1)),
            Padding = ScaleThickness(5, 2, 5, 2),
            Child = thresholdInput,
        };
        var thresholdRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = ScaleThickness(0, 8, 0, 0),
        };
        thresholdRow.Children.Add(new TextBlock
        {
            Text = Ui("超过 ", "Over "),
            FontFamily = FontForChinese(),
            FontSize = ScaleValue(12.4),
            FontWeight = FontWeights.Medium,
            Foreground = CreateFrozenBrush(Color.FromRgb(248, 250, 255)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        thresholdRow.Children.Add(thresholdInputShell);
        thresholdRow.Children.Add(new TextBlock
        {
            Text = Ui(" 分钟的任务启动提示音", " minutes: play sound"),
            FontFamily = FontForChinese(),
            FontSize = ScaleValue(12.4),
            FontWeight = FontWeights.Medium,
            Foreground = CreateFrozenBrush(Color.FromRgb(248, 250, 255)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var gatedPanel = new StackPanel();
        gatedPanel.Children.Add(LabelPlate(Ui("音效类型", "Sound Type")));
        gatedPanel.Children.Add(soundSelector);
        gatedPanel.Children.Add(LabelPlate(Ui("时间阈值", "Time Threshold")));
        gatedPanel.Children.Add(thresholdRow);

        void UpdateGate()
        {
            var enabled = enabledDraft;
            gatedPanel.Opacity = enabled ? 1.0 : 0.36;
            soundSelector.IsEnabled = enabled;
            soundSelector.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
            thresholdInput.IsEnabled = enabled;
            thresholdInputShell.IsEnabled = enabled;
            if (!enabled)
            {
                soundPopup.IsOpen = false;
            }
        }

        RefreshSegmentButtons();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = ScaleThickness(0, 13, 0, 0),
        };

        void CloseDialog(bool apply)
        {
            if (apply)
            {
                var enabled = enabledDraft;
                var choice = selectedChoiceDraft;
                var minutes = _completionSoundThresholdMinutes;
                if (enabled && !TryParseCompletionSoundThresholdMinutes(thresholdInput.Text, out minutes))
                {
                    thresholdInput.Focus();
                    thresholdInput.SelectAll();
                    return;
                }

                _dingDongEnabled = enabled;
                _completionSoundChoiceId = choice.Id;
                _completionSoundThresholdMinutes = Math.Clamp(minutes, 0, 1440);
                SaveUiSettings();
                DebugLog("completion_sound_settings", new
                {
                    enabled = _dingDongEnabled,
                    sound = _completionSoundChoiceId,
                    thresholdMinutes = Math.Round(_completionSoundThresholdMinutes, 2)
                });
            }

            dialog.Close();
        }

        buttons.Children.Add(CreateScaleDialogButton(Ui("取消", "Cancel"), () => CloseDialog(false)));
        buttons.Children.Add(CreateScaleDialogButton(Ui("确定", "OK"), () => CloseDialog(true)));

        var stack = new StackPanel();
        stack.Children.Add(LabelPlate(Ui("提示音效", "Completion Sound")));
        stack.Children.Add(modeRow);
        stack.Children.Add(gatedPanel);
        stack.Children.Add(buttons);

        var shell = new Grid
        {
            Children =
            {
                new Border
                {
                    CornerRadius = ScaleCornerRadius(16),
                    Padding = ScaleThickness(16, 14, 16, 14),
                    Background = CreateContextMenuBrush(),
                    BorderBrush = CreateFrozenBrush(Color.FromArgb(104, 205, 214, 235)),
                    BorderThickness = new Thickness(ScaleValue(1)),
                    IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(4, 8, 18),
                        BlurRadius = ScaleValue(22),
                        ShadowDepth = ScaleValue(8),
                        Opacity = 0.34,
                    },
                },
                new Border
                {
                    CornerRadius = ScaleCornerRadius(16),
                    Padding = ScaleThickness(16, 14, 16, 14),
                    Background = CreateContextMenuBrush(),
                    BorderBrush = CreateFrozenBrush(Color.FromArgb(104, 205, 214, 235)),
                    BorderThickness = new Thickness(ScaleValue(1)),
                    Child = stack,
                },
            },
        };
        TextOptions.SetTextFormattingMode(shell, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(shell, TextRenderingMode.Auto);
        TextOptions.SetTextHintingMode(shell, TextHintingMode.Fixed);
        dialog.Content = shell;

        bool IsDescendantOfSoundSelector(DependencyObject? source)
        {
            var current = source;
            while (current is not null)
            {
                if (ReferenceEquals(current, soundSelector))
                {
                    return true;
                }

                DependencyObject? parent = null;
                try
                {
                    parent = VisualTreeHelper.GetParent(current);
                }
                catch
                {
                }

                current = parent ?? LogicalTreeHelper.GetParent(current);
            }

            return false;
        }

        dialog.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (soundPopup.IsOpen &&
                e.OriginalSource is DependencyObject source &&
                !IsDescendantOfSoundSelector(source))
            {
                soundPopup.IsOpen = false;
            }
        };
        dialog.Deactivated += (_, _) => soundPopup.IsOpen = false;
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CloseDialog(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseDialog(false);
                e.Handled = true;
            }
        };
        dialog.Loaded += (_, _) =>
        {
            UpdateGate();
            if (_dingDongEnabled)
            {
                soundSelector.Focus();
            }
            else
            {
                offOption?.Focus();
            }
        };
        dialog.ShowDialog();
    }

    private Border CreateScaleDialogButton(string text, Action action)
    {
        var row = CreateContextMenuRow(text, action);
        row.MinWidth = ScaleValue(58);
        row.Margin = ScaleThickness(4, 0, 4, 0);
        return row;
    }

    private static bool TryParseScalePercent(string text, out double percent)
    {
        var normalized = (text ?? "").Trim().TrimEnd('%').Trim();
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out percent) ||
            double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            return true;
        }

        percent = 100;
        return false;
    }

    private static bool TryParseCompletionSoundThresholdMinutes(string text, out double minutes)
    {
        var normalized = (text ?? "").Trim();
        if ((double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out minutes) ||
             double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes)) &&
            minutes >= 0)
        {
            return true;
        }

        minutes = DefaultCompletionSoundThresholdMinutes;
        return false;
    }

    private Border CreateContextMenuRow(string text, Action action)
    {
        return CreateContextMenuRow(text, action, UiScale);
    }

    private static Border CreateContextMenuRow(string text, Action action, double scale)
    {
        var idleBrush = CreateFrozenBrush(Color.FromArgb(0, 255, 255, 255));
        var hoverBrush = CreateContextMenuRowHoverBrush();
        var row = new Border
        {
            CornerRadius = ScaleCornerRadius(10, scale),
            Padding = ScaleThickness(9, 8, 9, 8.5, scale),
            Margin = ScaleThickness(0, 1, 0, 1, scale),
            Background = idleBrush,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text,
                Foreground = CreateFrozenBrush(Color.FromRgb(229, 234, 244)),
                FontFamily = FontForChinese(),
                FontSize = 13.4 * scale,
                FontWeight = FontWeights.Medium,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            },
        };
        row.MouseEnter += (_, _) => row.Background = hoverBrush;
        row.MouseLeave += (_, _) => row.Background = idleBrush;
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return row;
    }

    private static Brush CreateContextMenuBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.02, 0.0),
            EndPoint = new Point(1.0, 1.0),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(238, 20, 24, 34), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(232, 12, 16, 25), 0.48));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(238, 25, 30, 42), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateContextMenuRowHoverBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(18, 255, 255, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(54, 136, 148, 188), 0.52));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(14, 255, 255, 255), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateDialogHeadingPlateBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(48, 232, 237, 248), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(28, 196, 205, 226), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(38, 236, 241, 250), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateDialogFieldBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(230, 15, 18, 26), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(236, 8, 11, 18), 0.58));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(232, 18, 22, 31), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateSegmentIdleBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(50, 236, 241, 250), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(24, 164, 174, 206), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateSegmentHoverBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(72, 244, 248, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(38, 176, 188, 224), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateSegmentSelectedBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(116, 236, 242, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(70, 158, 172, 216), 0.52));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(96, 245, 248, 255), 1.00));
        brush.Freeze();
        return brush;
    }

    private Border CreateSegmentRadioMarker(bool selected)
    {
        var outerSize = ScaleValue(16);
        var innerSize = ScaleValue(8.2);
        var outer = new Border
        {
            Width = outerSize,
            Height = outerSize,
            CornerRadius = new CornerRadius(outerSize / 2.0),
            Margin = ScaleThickness(0, 0, 7, 0),
            Background = CreateRadioOuterGlassBrush(selected),
            BorderBrush = CreateFrozenBrush(selected ? Color.FromArgb(174, 246, 249, 255) : Color.FromArgb(118, 224, 231, 246)),
            BorderThickness = new Thickness(ScaleValue(1)),
            IsHitTestVisible = false,
            Child = new Border
            {
                Width = innerSize,
                Height = innerSize,
                CornerRadius = new CornerRadius(innerSize / 2.0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = selected ? CreateRadioInnerFillBrush() : Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Opacity = selected ? 1.0 : 0.0,
            },
        };
        return outer;
    }

    private static Brush CreateRadioOuterGlassBrush(bool selected)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.42, 0.34),
            GradientOrigin = new Point(0.32, 0.24),
            RadiusX = 0.92,
            RadiusY = 0.92,
        };
        if (selected)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(128, 244, 248, 255), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(78, 160, 178, 226), 0.54));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(44, 9, 13, 22), 1.00));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(72, 244, 248, 255), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(34, 134, 146, 184), 0.58));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(30, 5, 8, 14), 1.00));
        }

        brush.Freeze();
        return brush;
    }

    private static Brush CreateRadioInnerFillBrush()
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.46, 0.38),
            GradientOrigin = new Point(0.34, 0.25),
            RadiusX = 0.86,
            RadiusY = 0.86,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(246, 255, 255, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(222, 216, 226, 250), 0.50));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(188, 128, 146, 214), 1.00));
        brush.Freeze();
        return brush;
    }

    private Grid CreateSoundPreviewButton(CompletionSoundChoice choice)
    {
        var outerSize = ScaleValue(28);
        var circleSize = ScaleValue(24);
        var button = new Grid
        {
            Width = outerSize,
            Height = outerSize,
            Margin = ScaleThickness(5, 0, 0, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = CreateControlToolTip(Ui("试听", "Preview")),
        };
        var glass = new Border
        {
            Width = circleSize,
            Height = circleSize,
            CornerRadius = new CornerRadius(circleSize / 2.0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = CreateSoundPreviewGlassBrush(false),
            BorderBrush = CreateSoundPreviewRimBrush(false),
            BorderThickness = new Thickness(ScaleValue(1.15)),
            IsHitTestVisible = false,
        };
        var playIcon = new TextBlock
        {
            Text = "\uE768",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = ScaleValue(10.8),
            FontWeight = FontWeights.Normal,
            Foreground = CreateFrozenBrush(Color.FromRgb(244, 248, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = ScaleThickness(1.2, 0.1, 0, 0),
            IsHitTestVisible = false,
        };
        var pressedInside = false;
        button.Children.Add(glass);
        button.Children.Add(playIcon);
        button.MouseEnter += (_, _) =>
        {
            glass.Background = CreateSoundPreviewGlassBrush(true);
            glass.BorderBrush = CreateSoundPreviewRimBrush(true);
        };
        button.MouseLeave += (_, _) =>
        {
            pressedInside = false;
            glass.Background = CreateSoundPreviewGlassBrush(false);
            glass.BorderBrush = CreateSoundPreviewRimBrush(false);
        };
        button.MouseLeftButtonDown += (_, e) =>
        {
            if (!IsInsideSoundPreviewCircle(e.GetPosition(button), outerSize))
            {
                return;
            }

            pressedInside = true;
            e.Handled = true;
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressedInside)
            {
                return;
            }

            pressedInside = false;
            e.Handled = true;
            if (IsInsideSoundPreviewCircle(e.GetPosition(button), outerSize))
            {
                PlayCompletionSoundPreview(choice);
            }
        };
        return button;
    }

    private static bool IsInsideSoundPreviewCircle(Point point, double boxSize)
    {
        var center = boxSize / 2.0;
        var radius = boxSize * 0.42;
        var dx = point.X - center;
        var dy = point.Y - center;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static Brush CreateSoundPreviewGlassBrush(bool hover)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.40, 0.32),
            GradientOrigin = new Point(0.28, 0.20),
            RadiusX = 0.92,
            RadiusY = 0.92,
        };
        if (hover)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(142, 250, 252, 255), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(76, 168, 184, 228), 0.52));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(48, 13, 17, 28), 1.00));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(96, 246, 249, 255), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(46, 132, 146, 188), 0.58));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(36, 8, 11, 19), 1.00));
        }

        brush.Freeze();
        return brush;
    }

    private static Brush CreateSoundPreviewRimBrush(bool hover)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.22, 0.02),
            EndPoint = new Point(0.92, 1.0),
        };
        if (hover)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(188, 250, 252, 255), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(106, 176, 190, 230), 0.46));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(66, 92, 104, 138), 1.00));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(146, 240, 245, 255), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(72, 146, 160, 198), 0.50));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(44, 64, 73, 100), 1.00));
        }

        brush.Freeze();
        return brush;
    }

    private void ConfigureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        _trayStatusIcon ??= CreateTrayStatusIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayStatusIcon,
            Text = "Codexstar",
            Visible = true,
        };
        _trayIcon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                Dispatcher.Invoke(ShowTrayContextMenu);
            }
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void ShowTrayContextMenu()
    {
        CloseTrayContextMenu();

        const double trayMenuScale = 1.0;
        var menu = new Window
        {
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = true,
            SnapsToDevicePixels = true,
            Content = CreateContextMenuShell(CreateContextMenuStack(trayMenuScale, CloseTrayContextMenu, includeShow: true), trayMenuScale),
        };
        TextOptions.SetTextFormattingMode(menu, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(menu, TextRenderingMode.Auto);
        TextOptions.SetTextHintingMode(menu, TextHintingMode.Fixed);
        menu.Deactivated += (_, _) => CloseTrayContextMenu();
        menu.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseTrayContextMenu();
                e.Handled = true;
            }
        };
        menu.Loaded += (_, _) => PositionTrayContextMenu(menu);
        _trayContextMenu = menu;
        menu.Show();
        InstallTrayContextMenuMouseHook();
        menu.Focus();
    }

    private void PositionTrayContextMenu(Window menu)
    {
        var cursor = Forms.Cursor.Position;
        var dpi = VisualTreeHelper.GetDpi(this);
        var left = cursor.X / dpi.DpiScaleX - 8;
        var top = cursor.Y / dpi.DpiScaleY - 8;
        var area = SystemParameters.WorkArea;

        if (left + menu.ActualWidth > area.Right - 8)
        {
            left = area.Right - menu.ActualWidth - 8;
        }

        if (top + menu.ActualHeight > area.Bottom - 8)
        {
            top = area.Bottom - menu.ActualHeight - 8;
        }

        menu.Left = Math.Max(area.Left + 8, left);
        menu.Top = Math.Max(area.Top + 8, top);
    }

    private void CloseTrayContextMenu()
    {
        UninstallTrayContextMenuMouseHook();
        if (_trayContextMenu is null)
        {
            return;
        }

        var menu = _trayContextMenu;
        _trayContextMenu = null;
        menu.Close();
    }

    private void InstallTrayContextMenuMouseHook()
    {
        if (_trayMouseHook != IntPtr.Zero)
        {
            return;
        }

        _trayMouseHookProc = TrayMouseHookCallback;
        _trayMouseHook = SetWindowsHookEx(WH_MOUSE_LL, _trayMouseHookProc, GetModuleHandle(null), 0);
        if (_trayMouseHook == IntPtr.Zero)
        {
            DebugLog("tray_menu_mouse_hook_failed", new { error = Marshal.GetLastWin32Error() });
        }
    }

    private void UninstallTrayContextMenuMouseHook()
    {
        if (_trayMouseHook == IntPtr.Zero)
        {
            _trayMouseHookProc = null;
            return;
        }

        UnhookWindowsHookEx(_trayMouseHook);
        _trayMouseHook = IntPtr.Zero;
        _trayMouseHookProc = null;
    }

    private IntPtr TrayMouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsMouseButtonDownMessage(wParam))
        {
            var hook = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            Dispatcher.BeginInvoke(() =>
            {
                if (_trayContextMenu is null || IsScreenPointInsideWindow(_trayContextMenu, hook.pt.x, hook.pt.y))
                {
                    return;
                }

                CloseTrayContextMenu();
            });
        }

        return CallNextHookEx(_trayMouseHook, nCode, wParam, lParam);
    }

    private static bool IsMouseButtonDownMessage(IntPtr message)
    {
        var value = message.ToInt32();
        return value is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN;
    }

    private static bool IsScreenPointInsideWindow(Window window, int x, int y)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
        {
            return false;
        }

        return x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
    }

    private void HideToTray()
    {
        Hide();
        UpdatePaintTimerInterval();
        DebugLog("hide_to_tray", new { collapsed = _isCollapsed });
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        UpdatePaintTimerInterval();
        Render();
    }

    private void DisposeRuntimeResources()
    {
        _pollTimer.Stop();
        _paintTimer.Stop();
        _globalStateDebounceTimer.Stop();
        _modeTransitionTimer?.Stop();

        foreach (var timer in _exitTimers.Values.ToList())
        {
            timer.Stop();
        }

        foreach (var timer in _pendingCompletionSoundTimers.Values.ToList())
        {
            timer.Stop();
        }
        _pendingCompletionSoundTimers.Clear();
        _pendingCompletionSoundThreadIds.Clear();

        _sessionWatcher?.Dispose();
        _sessionWatcher = null;
        _stateWatcher?.Dispose();
        _stateWatcher = null;
        _globalWatcher?.Dispose();
        _globalWatcher = null;

        foreach (var timeout in _completionPlayerTimeouts.Values.ToList())
        {
            timeout.Stop();
        }

        foreach (var player in _activeCompletionPlayers.ToList())
        {
            CleanupCompletionPlayer(player);
        }

        DisposeTrayIcon();
    }

    private void DisposeTrayIcon()
    {
        CloseTrayContextMenu();
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
        _trayStatusIcon?.Dispose();
        _trayStatusIcon = null;
    }

    private static Drawing.Icon CreateTrayStatusIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
        graphics.Clear(Drawing.Color.Transparent);

        using (var shadow = new Drawing.SolidBrush(Drawing.Color.FromArgb(72, 0, 0, 0)))
        {
            graphics.FillEllipse(shadow, 1.6f, 2.7f, 28.8f, 28.8f);
        }

        using (var shell = new Drawing2D.LinearGradientBrush(
                   new Drawing.RectangleF(1.9f, 1.3f, 28.6f, 28.6f),
                   Drawing.Color.FromArgb(232, 16, 19, 28),
                   Drawing.Color.FromArgb(232, 35, 39, 56),
                   45f))
        {
            graphics.FillEllipse(shell, 1.9f, 1.3f, 28.6f, 28.6f);
        }

        using (var rim = new Drawing.Pen(Drawing.Color.FromArgb(178, 167, 174, 214), 1.65f))
        {
            graphics.DrawEllipse(rim, 2.7f, 2.1f, 27.0f, 27.0f);
        }

        using (var glowPath = CreateEllipsePath(6.7f, 6.0f, 19.4f, 19.4f))
        using (var innerGlow = new Drawing2D.PathGradientBrush(glowPath))
        {
            innerGlow.CenterColor = Drawing.Color.FromArgb(235, 128, 121, 224);
            innerGlow.SurroundColors = new[] { Drawing.Color.FromArgb(18, 128, 121, 224) };
            graphics.FillEllipse(innerGlow, 6.7f, 6.0f, 19.4f, 19.4f);
        }

        using (var core = new Drawing.SolidBrush(Drawing.Color.FromArgb(245, 160, 154, 235)))
        {
            graphics.FillEllipse(core, 10.4f, 9.8f, 11.8f, 11.8f);
        }

        using (var highlight = new Drawing.SolidBrush(Drawing.Color.FromArgb(180, 232, 236, 255)))
        {
            graphics.FillEllipse(highlight, 12.0f, 10.8f, 3.0f, 3.0f);
        }

        using (var pixel = new Drawing.SolidBrush(Drawing.Color.FromArgb(190, 126, 190, 236)))
        {
            graphics.FillRectangle(pixel, 25.3f, 5.9f, 2.9f, 2.9f);
            graphics.FillRectangle(pixel, 4.1f, 25.4f, 2.7f, 2.7f);
        }

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var icon = Drawing.Icon.FromHandle(iconHandle);
            return (Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private static Drawing2D.GraphicsPath CreateEllipsePath(float x, float y, float width, float height)
    {
        var path = new Drawing2D.GraphicsPath();
        path.AddEllipse(x, y, width, height);
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private void ConfigureWatchers()
    {
        try
        {
            if (Directory.Exists(_sessionsRoot))
            {
                _sessionWatcher = new FileSystemWatcher(_sessionsRoot)
                {
                    IncludeSubdirectories = true,
                    Filter = "*.jsonl",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };
                _sessionWatcher.Changed += (_, e) => QueueSessionFileFromWatcher(e.FullPath);
                _sessionWatcher.Created += (_, e) => QueueSessionFileFromWatcher(e.FullPath);
            }
        }
        catch
        {
            // Polling keeps the overlay usable if the watcher cannot be created.
        }

        try
        {
            _stateWatcher = new FileSystemWatcher(_stateDir)
            {
                Filter = "state.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _stateWatcher.Changed += (_, _) => Dispatcher.BeginInvoke(ReadManualState);
            _stateWatcher.Created += (_, _) => Dispatcher.BeginInvoke(ReadManualState);
        }
        catch
        {
        }

        try
        {
            _globalWatcher = new FileSystemWatcher(_codexRoot)
            {
                Filter = ".codex-global-state.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _globalWatcher.Changed += (_, _) => Dispatcher.BeginInvoke(ScheduleGlobalStateRefresh);
        }
        catch
        {
        }
    }

    private void ScheduleGlobalStateRefresh()
    {
        _globalStateDebounceTimer.Stop();
        _globalStateDebounceTimer.Start();
    }

    private void RefreshGlobalStateFromWatcher()
    {
        LoadUnreadThreadIds(force: true);
        ReconcileAcknowledgements();
        PruneStaleTasks();
        Render();
    }

    private void QueueSessionFile(string path)
    {
        lock (_pendingFiles)
        {
            _pendingFiles.Add(path);
        }
    }

    private void QueueSessionFileFromWatcher(string path)
    {
        _lastSessionWatcherEventUtc = DateTime.UtcNow;
        QueueSessionFile(path);
    }

    private void Poll()
    {
        LoadThreadTitles();
        LoadGoals();
        SyncTaskTitles();
        SyncGoalTasks();
        SweepSupersededTerminalTasks();
        LoadUnreadThreadIds();
        ReadManualState();
        QueueActiveSessionFiles();
        QueueRecentlyChangedSessionFiles();

        List<string> files;
        lock (_pendingFiles)
        {
            files = _pendingFiles.ToList();
            _pendingFiles.Clear();
        }

        foreach (var path in files)
        {
            ReadSessionFile(path, fromStart: false);
        }

        ReconcileAcknowledgements();
        PruneStaleTasks();
        Render();
    }

    private void QueueActiveSessionFiles()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastActiveFilePollUtc).TotalMilliseconds < 850)
        {
            return;
        }

        _lastActiveFilePollUtc = now;
        foreach (var path in _activeTurnsByFile.Keys.ToList())
        {
            QueueSessionFile(path);
        }
    }

    private void QueueRecentlyChangedSessionFiles()
    {
        var now = DateTime.UtcNow;
        var hasActiveTurns = _activeTurnsByFile.Count > 0;
        var hasRecentWatcherActivity = (now - _lastSessionWatcherEventUtc).TotalSeconds < 12;
        var scanIntervalSeconds = hasActiveTurns || hasRecentWatcherActivity ? 3 : 60;
        if ((now - _lastRecentFileScanUtc).TotalSeconds < scanIntervalSeconds)
        {
            return;
        }

        var since = _lastRecentFileScanUtc == DateTime.MinValue
            ? now.AddSeconds(-30)
            : _lastRecentFileScanUtc.AddSeconds(-2);
        _lastRecentFileScanUtc = now;

        try
        {
            if (!Directory.Exists(_sessionsRoot))
            {
                return;
            }

            var limit = hasActiveTurns || hasRecentWatcherActivity ? 16 : 8;
            foreach (var file in new DirectoryInfo(_sessionsRoot)
                         .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                         .Where(f => f.LastWriteTimeUtc >= since)
                         .OrderByDescending(f => f.LastWriteTimeUtc)
                         .Take(limit))
            {
                QueueSessionFile(file.FullName);
            }
        }
        catch (Exception ex)
        {
            DebugLog("recent_scan_failed", new { error = ex.Message });
        }
    }

    private void PruneStaleTasks()
    {
        var now = DateTime.UtcNow;
        var staleIds = _tasks.Values
            .Where(t =>
                t.Status == TaskVisualStatus.Working && t.Goal?.IsActive != true && (now - t.StartedAt).TotalHours > 2 ||
                t.Status == TaskVisualStatus.Error && (now - t.LastEventAt).TotalMinutes > 15 ||
                t.Status == TaskVisualStatus.Done && !t.WasUnread && !_unreadThreadIds.Contains(t.ThreadId) && IsValidCompletedAt(t.CompletedAt) && (now - t.CompletedAt).TotalSeconds > 45)
            .Select(t => t.TurnId)
            .ToList();

        foreach (var id in staleIds)
        {
            _tasks.Remove(id);
        }
    }

    private void BootstrapRecentSessions()
    {
        if (!Directory.Exists(_sessionsRoot))
        {
            return;
        }

        var startedAtUtc = DateTime.UtcNow;
        var cutoff = DateTime.Now.AddHours(-24);
        var files = new DirectoryInfo(_sessionsRoot)
            .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .Where(f => f.LastWriteTime >= cutoff)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(30)
            .Select(f => f.FullName)
            .ToList();

        var previousSuppression = _suppressCompletionSound;
        var previousReplaySuppression = _suppressSessionReplayDebug;
        _suppressCompletionSound = true;
        _suppressSessionReplayDebug = true;
        _isBootstrappingSessions = true;
        try
        {
            foreach (var path in files)
            {
                ReadSessionFile(path, fromStart: true);
            }
        }
        finally
        {
            _suppressCompletionSound = previousSuppression;
            _suppressSessionReplayDebug = previousReplaySuppression;
            _isBootstrappingSessions = false;
        }

        PruneStaleTasks();
        DebugLog("bootstrap_complete", new
        {
            files = files.Count,
            elapsedMs = Math.Round((DateTime.UtcNow - startedAtUtc).TotalMilliseconds),
            weeklyRemaining = _rateLimits is null ? (double?)null : Math.Round(_rateLimits.WeeklyRemainingPercent),
            fiveHourRemaining = _rateLimits is null ? (double?)null : Math.Round(_rateLimits.FiveHourRemainingPercent),
            observedAtUtc = _rateLimits?.ObservedAtUtc,
        });
    }

    private void ReadSessionFile(string path, bool fromStart)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var fileInfo = new FileInfo(path);
            var length = fileInfo.Length;
            long start;
            if (fromStart)
            {
                start = 0;
            }
            else if (!_fileOffsets.TryGetValue(path, out start))
            {
                // Existing session files can be touched by metadata updates or model changes.
                // Attach at EOF so their historical task events are never replayed.
                var isNewDuringThisRuntime = fileInfo.CreationTimeUtc >= _runtimeStartedAtUtc.AddSeconds(-5);
                start = isNewDuringThisRuntime ? 0 : length;
                if (!isNewDuringThisRuntime)
                {
                    DebugLog("session_tail_attached", new { path, length, createdAtUtc = fileInfo.CreationTimeUtc });
                }
            }
            if (start > length)
            {
                start = 0;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    ProcessSessionLine(path, line);
                }
            }

            _fileOffsets[path] = length;
            TrimFileOffsets();
        }
        catch
        {
        }
    }

    private void ProcessSessionLine(string path, string line)
    {
        if (!line.Contains("\"type\":\"event_msg\"", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "event_msg")
            {
                return;
            }

            if (!root.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("type", out var payloadTypeProp))
            {
                return;
            }

            var payloadType = payloadTypeProp.GetString();
            var timestamp = ReadTimestamp(root);
            if (TryGetProperty(root, out var rateLimits, "rate_limits", "rateLimits") ||
                TryGetProperty(payload, out rateLimits, "rate_limits", "rateLimits"))
            {
                UpdateRateLimits(rateLimits, timestamp);
            }

            if (payloadType is not ("task_started" or "task_complete" or "turn_aborted"))
            {
                return;
            }

            if (!payload.TryGetProperty("turn_id", out var turnProp))
            {
                return;
            }

            var turnId = turnProp.GetString();
            if (string.IsNullOrWhiteSpace(turnId))
            {
                return;
            }

            var eventKey = $"{payloadType}:{turnId}:{timestamp:o}";
            if (!RememberSeenEvent(eventKey))
            {
                return;
            }

            var threadId = GetThreadIdFromPath(path);
            if (string.IsNullOrWhiteSpace(threadId))
            {
                threadId = turnId;
            }

            if (payloadType == "task_started")
            {
                var startedAt = ReadUnixSeconds(payload, "started_at") ?? timestamp;
                MarkTurnActive(path, turnId);
                DebugLogSessionEvent("task_started", new { turnId, threadId, path, startedAt });
                CancelPendingCompletionSoundsForThread(threadId, "followup_started");
                RemoveSupersededTasks(threadId, GetThreadTitle(threadId), turnId);
                var task = UpsertTask(turnId, threadId, startedAt, TaskVisualStatus.Working, null, null);
                task.ObservedLiveStart = !_isBootstrappingSessions;
                return;
            }

            if (payloadType == "task_complete")
            {
                var completedAt = ReadUnixSeconds(payload, "completed_at") ?? timestamp;
                TimeSpan? duration = payload.TryGetProperty("duration_ms", out var durationProp) && durationProp.TryGetInt64(out var durationMs)
                    ? TimeSpan.FromMilliseconds(durationMs)
                    : null;
                MarkTurnInactive(path, turnId);
                DebugLogSessionEvent("task_complete", new { turnId, threadId, path, completedAt, durationMs = duration?.TotalMilliseconds });
                if (_isBootstrappingSessions)
                {
                    _tasks.Remove(turnId);
                    return;
                }

                if (!_tasks.TryGetValue(turnId, out var existingTask))
                {
                    DebugLog("terminal_event_ignored", new { type = payloadType, turnId, threadId, reason = "no_observed_start" });
                    _tasks.Remove(turnId);
                    return;
                }

                var task = UpsertTask(turnId, threadId, completedAt - (duration ?? TimeSpan.Zero), TaskVisualStatus.Done, completedAt, duration);
                if (existingTask.ObservedLiveStart)
                {
                    QueueCompletionSoundAfterSettling(task);
                }
                return;
            }

            if (payloadType == "turn_aborted")
            {
                MarkTurnInactive(path, turnId);
                DebugLogSessionEvent("turn_aborted", new { turnId, threadId, path });
                if (_isBootstrappingSessions)
                {
                    _tasks.Remove(turnId);
                    return;
                }

                if (!_tasks.TryGetValue(turnId, out _))
                {
                    DebugLog("terminal_event_ignored", new { type = payloadType, turnId, threadId, reason = "no_observed_start" });
                    _tasks.Remove(turnId);
                    return;
                }

                var task = UpsertTask(turnId, threadId, timestamp, TaskVisualStatus.Error, timestamp, null);
                task.Message = "已中止";
            }
        }
        catch
        {
        }
    }

    private void MarkTurnActive(string path, string turnId)
    {
        if (!_activeTurnsByFile.TryGetValue(path, out var turns))
        {
            turns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _activeTurnsByFile[path] = turns;
        }

        turns.Add(turnId);
    }

    private void MarkTurnInactive(string path, string turnId)
    {
        if (!_activeTurnsByFile.TryGetValue(path, out var turns))
        {
            return;
        }

        turns.Remove(turnId);
        if (turns.Count == 0)
        {
            _activeTurnsByFile.Remove(path);
            TrimFileOffsets();
        }
    }

    private bool RememberSeenEvent(string eventKey)
    {
        if (!_seenEvents.Add(eventKey))
        {
            return false;
        }

        _seenEventOrder.Enqueue(eventKey);
        while (_seenEventOrder.Count > MaxSeenEvents)
        {
            for (var i = 0; i < SeenEventsTrimBatch && _seenEventOrder.Count > MaxSeenEvents; i++)
            {
                _seenEvents.Remove(_seenEventOrder.Dequeue());
            }
        }

        return true;
    }

    private bool RememberCompletionSoundedTurn(string turnId)
    {
        if (!_completionSoundedTurns.Add(turnId))
        {
            return false;
        }

        _completionSoundedTurnOrder.Enqueue(turnId);
        while (_completionSoundedTurnOrder.Count > MaxCompletionSoundedTurns)
        {
            for (var i = 0; i < CompletionSoundTrimBatch && _completionSoundedTurnOrder.Count > MaxCompletionSoundedTurns; i++)
            {
                _completionSoundedTurns.Remove(_completionSoundedTurnOrder.Dequeue());
            }
        }

        return true;
    }

    private void TrimFileOffsets()
    {
        if (_fileOffsets.Count <= MaxInactiveFileOffsets)
        {
            return;
        }

        var removeCount = _fileOffsets.Count - MaxInactiveFileOffsets;
        foreach (var path in _fileOffsets.Keys.Where(path => !_activeTurnsByFile.ContainsKey(path)).Take(removeCount).ToList())
        {
            _fileOffsets.Remove(path);
        }
    }

    private TaskState UpsertTask(string turnId, string threadId, DateTime startedAt, TaskVisualStatus status, DateTime? completedAt, TimeSpan? duration)
    {
        if (!_tasks.TryGetValue(turnId, out var task))
        {
            task = new TaskState(turnId, threadId, GetThreadTitle(threadId), startedAt);
            _tasks[turnId] = task;
        }

        task.ThreadId = threadId;
        task.Title = GetThreadTitle(threadId);
        if (task.StartedAt == default || status == TaskVisualStatus.Working)
        {
            task.StartedAt = startedAt;
        }

        if (status != TaskVisualStatus.Working || task.Status != TaskVisualStatus.Done)
        {
            task.Status = status;
        }

        if (completedAt is not null)
        {
            task.CompletedAt = completedAt.Value;
            task.LastEventAt = completedAt.Value;
        }
        else
        {
            task.LastEventAt = DateTime.UtcNow;
        }

        if (duration is not null)
        {
            task.Duration = duration.Value;
        }

        if (status == TaskVisualStatus.Done)
        {
            task.WasUnread = task.WasUnread || _unreadThreadIds.Contains(threadId);
            task.Message = "待验收";
        }

        return task;
    }

    private void TryPlayCompletionSound(TaskState task)
    {
        if (!_dingDongEnabled || _suppressCompletionSound || task.Status != TaskVisualStatus.Done)
        {
            return;
        }

        if (!RememberCompletionSoundedTurn(task.TurnId))
        {
            return;
        }

        var duration = GetTaskDuration(task);
        var thresholdSeconds = _completionSoundThresholdMinutes * 60.0;
        if (duration.TotalSeconds < thresholdSeconds)
        {
            DebugLog("completion_sound_skipped", new
            {
                turnId = task.TurnId,
                seconds = Math.Round(duration.TotalSeconds, 1),
                thresholdSeconds = Math.Round(thresholdSeconds, 1)
            });
            return;
        }

        var soundPath = ResolveCompletionSoundPath();
        if (!File.Exists(soundPath))
        {
            DebugLog("completion_sound_missing", new { turnId = task.TurnId, path = soundPath });
            return;
        }

        DebugLog("completion_sound_play", new
        {
            turnId = task.TurnId,
            seconds = Math.Round(duration.TotalSeconds, 1),
            sound = GetCompletionSoundDisplayName(GetCompletionSoundChoice(_completionSoundChoiceId)),
            path = soundPath
        });

        PlayCompletionSound(soundPath, task.TurnId);
    }

    private void QueueCompletionSoundAfterSettling(TaskState task)
    {
        CancelPendingCompletionSound(task.TurnId);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CompletionSoundSettleDelayMilliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _pendingCompletionSoundTimers.Remove(task.TurnId);
            _pendingCompletionSoundThreadIds.Remove(task.TurnId);

            if (_tasks.TryGetValue(task.TurnId, out var currentTask) &&
                ReferenceEquals(currentTask, task) &&
                currentTask.Status == TaskVisualStatus.Done &&
                currentTask.ObservedLiveStart)
            {
                TryPlayCompletionSound(currentTask);
            }
        };

        _pendingCompletionSoundTimers[task.TurnId] = timer;
        _pendingCompletionSoundThreadIds[task.TurnId] = task.ThreadId;
        timer.Start();
    }

    private void CancelPendingCompletionSoundsForThread(string threadId, string reason)
    {
        foreach (var turnId in _pendingCompletionSoundThreadIds
                     .Where(pair => string.Equals(pair.Value, threadId, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            DebugLog("completion_sound_cancelled", new { turnId, threadId, reason });
            CancelPendingCompletionSound(turnId);
        }
    }

    private void CancelPendingCompletionSound(string turnId)
    {
        if (_pendingCompletionSoundTimers.Remove(turnId, out var timer))
        {
            timer.Stop();
        }

        _pendingCompletionSoundThreadIds.Remove(turnId);
    }

    private static TimeSpan GetTaskDuration(TaskState task)
    {
        if (task.Duration > TimeSpan.Zero)
        {
            return task.Duration;
        }

        if (task.CompletedAt != default && task.StartedAt != default && task.CompletedAt > task.StartedAt)
        {
            return task.CompletedAt - task.StartedAt;
        }

        return TimeSpan.Zero;
    }

    private void PlayCompletionSound(string soundPath, string turnId)
    {
        try
        {
            var player = new MediaPlayer
            {
                Volume = 1.0,
            };
            player.MediaEnded += (_, _) => CleanupCompletionPlayer(player);
            player.MediaFailed += (_, e) =>
            {
                DebugLog("completion_sound_failed", new { turnId, error = e.ErrorException.Message });
                CleanupCompletionPlayer(player);
            };
            _activeCompletionPlayers.Add(player);
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CompletionPlayerTimeoutSeconds) };
            timeout.Tick += (_, _) =>
            {
                timeout.Stop();
                CleanupCompletionPlayer(player);
            };
            _completionPlayerTimeouts[player] = timeout;
            timeout.Start();
            TrimCompletionPlayers();
            player.Open(new Uri(soundPath, UriKind.Absolute));
            player.Play();
        }
        catch (Exception ex)
        {
            DebugLog("completion_sound_failed", new { turnId, error = ex.Message });
        }
    }

    private void PlayCompletionSoundPreview(CompletionSoundChoice choice)
    {
        var soundPath = IOPath.Combine(AppContext.BaseDirectory, choice.RelativePath);
        if (!File.Exists(soundPath))
        {
            DebugLog("completion_sound_preview_missing", new { sound = GetCompletionSoundDisplayName(choice), path = soundPath });
            return;
        }

        PlayCompletionSound(soundPath, $"preview:{choice.Id}");
    }

    private void CleanupCompletionPlayer(MediaPlayer player)
    {
        if (_completionPlayerTimeouts.Remove(player, out var timeout))
        {
            timeout.Stop();
        }

        try
        {
            player.Close();
        }
        catch
        {
        }

        _activeCompletionPlayers.Remove(player);
    }

    private void TrimCompletionPlayers()
    {
        while (_activeCompletionPlayers.Count > MaxActiveCompletionPlayers)
        {
            CleanupCompletionPlayer(_activeCompletionPlayers[0]);
        }
    }

    private string ResolveCompletionSoundPath()
    {
        var choice = GetCompletionSoundChoice(_completionSoundChoiceId);
        return IOPath.Combine(AppContext.BaseDirectory, choice.RelativePath);
    }

    private static CompletionSoundChoice GetCompletionSoundChoice(string? id)
    {
        return CompletionSoundChoices.FirstOrDefault(choice => string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase)) ??
               CompletionSoundChoices[0];
    }

    private void RemoveSupersededTasks(string threadId, string title, string activeTurnId)
    {
        var normalizedTitle = NormalizeTitle(title);
        var oldIds = _tasks.Values
            .Where(t => t.TurnId != activeTurnId &&
                        !_exitingCards.Contains(t.TurnId) &&
                        !t.TurnId.StartsWith("goal:", StringComparison.OrdinalIgnoreCase) &&
                        t.Status is TaskVisualStatus.Working or TaskVisualStatus.Done or TaskVisualStatus.Error or TaskVisualStatus.Input)
            .Where(t => t.ThreadId == threadId ||
                        (t.ThreadId != threadId &&
                         !string.IsNullOrWhiteSpace(normalizedTitle) &&
                         NormalizeTitle(t.Title) == normalizedTitle))
            .Select(t => t.TurnId)
            .ToList();

        foreach (var id in oldIds)
        {
            RemoveTask(id, exitUp: false);
        }
    }

    private void SyncTaskTitles()
    {
        foreach (var task in _tasks.Values)
        {
            if (task.ThreadId is "manual" or "idle")
            {
                continue;
            }

            task.Title = GetThreadTitle(task.ThreadId);
        }
    }

    private void SweepSupersededTerminalTasks()
    {
        var activeTasks = _tasks.Values
            .Where(t => t.Status == TaskVisualStatus.Working)
            .Select(t => new { t.ThreadId, t.Title, t.TurnId })
            .ToList();

        foreach (var task in activeTasks)
        {
            RemoveSupersededTasks(task.ThreadId, task.Title, task.TurnId);
        }
    }

    private void ReadManualState()
    {
        try
        {
            if (!File.Exists(_manualStatePath))
            {
                return;
            }

            var writeUtc = File.GetLastWriteTimeUtc(_manualStatePath);
            if (writeUtc <= _lastManualWriteUtc)
            {
                return;
            }

            _lastManualWriteUtc = writeUtc;
            using var fs = new FileStream(_manualStatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            if (!doc.RootElement.TryGetProperty("status", out var statusProp))
            {
                return;
            }

            var status = statusProp.GetString()?.Trim().ToLowerInvariant();
            var message = doc.RootElement.TryGetProperty("message", out var messageProp)
                ? messageProp.GetString()
                : null;

            if (status == "idle")
            {
                _tasks.Remove("manual");
                return;
            }

            var visualStatus = status switch
            {
                "working" => TaskVisualStatus.Working,
                "done" => TaskVisualStatus.Done,
                "input" => TaskVisualStatus.Input,
                "error" => TaskVisualStatus.Error,
                _ => TaskVisualStatus.Working,
            };

            var task = UpsertTask("manual", "manual", DateTime.UtcNow, visualStatus, visualStatus == TaskVisualStatus.Working ? null : DateTime.UtcNow, null);
            task.Title = "手动状态";
            task.Message = string.IsNullOrWhiteSpace(message) ? task.Message : message!;
        }
        catch
        {
        }
    }

    private void LoadUiSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(_settingsPath, System.Text.Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("quotaPinned", out var pinnedProp) &&
                pinnedProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _isQuotaPinned = pinnedProp.GetBoolean();
            }

            if (TryGetProperty(doc.RootElement, out var scaleProp, "scalePercent", "uiScalePercent") &&
                TryReadDouble(scaleProp, out var scalePercent))
            {
                _uiScalePercent = Math.Clamp(scalePercent, MinUiScalePercent, MaxUiScalePercent);
            }

            if (TryGetProperty(doc.RootElement, out var dingDongProp, "completionSoundEnabled", "dingDongEnabled", "dingDong") &&
                dingDongProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _dingDongEnabled = dingDongProp.GetBoolean();
            }

            if (TryGetProperty(doc.RootElement, out var quotaModeProp, "quotaPercentInRing", "showQuotaPercentInRing") &&
                quotaModeProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _showQuotaPercentInRing = quotaModeProp.GetBoolean();
            }

            if (TryGetProperty(doc.RootElement, out var externalBalancesProp, "showExternalBalances", "externalBalancesVisible") &&
                externalBalancesProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _showExternalBalances = externalBalancesProp.GetBoolean();
            }

            if (TryGetProperty(doc.RootElement, out var providerCountProp, "externalBalanceProviderCount") &&
                TryReadDouble(providerCountProp, out var providerCount))
            {
                _externalBalanceProviderCount = Math.Clamp((int)Math.Round(providerCount), 1, 2);
            }

            if (TryGetProperty(doc.RootElement, out var soundChoiceProp, "completionSoundChoiceId", "completionSoundId") &&
                soundChoiceProp.ValueKind == JsonValueKind.String)
            {
                _completionSoundChoiceId = GetCompletionSoundChoice(soundChoiceProp.GetString()).Id;
            }

            if (TryGetProperty(doc.RootElement, out var soundThresholdProp, "completionSoundThresholdMinutes") &&
                TryReadDouble(soundThresholdProp, out var thresholdMinutes))
            {
                _completionSoundThresholdMinutes = Math.Clamp(thresholdMinutes, 0, 1440);
            }

            if (TryGetProperty(doc.RootElement, out var languageProp, "uiLanguage", "language") &&
                languageProp.ValueKind == JsonValueKind.String)
            {
                var language = languageProp.GetString();
                _uiLanguage = language is not null &&
                              (language.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                               language.Equals("english", StringComparison.OrdinalIgnoreCase))
                    ? UiLanguage.English
                    : UiLanguage.Chinese;
            }
        }
        catch (Exception ex)
        {
            DebugLog("settings_load_failed", new { error = ex.Message });
        }
    }

    private void SaveUiSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                quotaPinned = _isQuotaPinned,
                quotaPercentInRing = _showQuotaPercentInRing,
                showExternalBalances = _showExternalBalances,
                externalBalanceProviderCount = _externalBalanceProviderCount,
                scalePercent = Math.Round(_uiScalePercent, 1),
                completionSoundEnabled = _dingDongEnabled,
                completionSoundChoiceId = GetCompletionSoundChoice(_completionSoundChoiceId).Id,
                completionSoundThresholdMinutes = Math.Round(_completionSoundThresholdMinutes, 2),
                uiLanguage = IsEnglishUi ? "en" : "zh",
                dingDongEnabled = _dingDongEnabled,
            });
            File.WriteAllText(_settingsPath, json, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            DebugLog("settings_save_failed", new { error = ex.Message });
        }
    }

    private void LoadThreadTitles(bool force = false)
    {
        try
        {
            if (!File.Exists(_sessionIndexPath))
            {
                return;
            }

            var info = new FileInfo(_sessionIndexPath);
            if (!force &&
                info.LastWriteTimeUtc <= _lastSessionIndexWriteUtc &&
                info.Length == _lastSessionIndexLength)
            {
                return;
            }

            using var fs = new FileStream(_sessionIndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("id", out var idProp) ||
                    !doc.RootElement.TryGetProperty("thread_name", out var nameProp))
                {
                    continue;
                }

                var id = idProp.GetString();
                var name = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                {
                    _threadTitles[id] = name.Trim();
                }
            }

            _lastSessionIndexWriteUtc = info.LastWriteTimeUtc;
            _lastSessionIndexLength = info.Length;
        }
        catch
        {
        }
    }

    private void LoadUnreadThreadIds(bool force = false)
    {
        try
        {
            if (!File.Exists(_globalStatePath))
            {
                return;
            }

            var info = new FileInfo(_globalStatePath);
            if (!force &&
                info.LastWriteTimeUtc <= _lastGlobalStateWriteUtc &&
                info.Length == _lastGlobalStateLength)
            {
                return;
            }

            using var fs = new FileStream(_globalStatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            _unreadThreadIds.Clear();

            if (!doc.RootElement.TryGetProperty("electron-persisted-atom-state", out var atomState) ||
                !atomState.TryGetProperty("unread-thread-ids-by-host-v1", out var unreadByHost) ||
                !unreadByHost.TryGetProperty("local", out var localUnread) ||
                localUnread.ValueKind != JsonValueKind.Array)
            {
                _lastGlobalStateWriteUtc = info.LastWriteTimeUtc;
                _lastGlobalStateLength = info.Length;
                return;
            }

            foreach (var item in localUnread.EnumerateArray())
            {
                var id = item.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _unreadThreadIds.Add(id);
                }
            }

            _lastGlobalStateWriteUtc = info.LastWriteTimeUtc;
            _lastGlobalStateLength = info.Length;
        }
        catch
        {
        }
    }

    private void LoadGoals(bool force = false)
    {
        try
        {
            if (!File.Exists(_goalsDbPath))
            {
                _goals.Clear();
                return;
            }

            var dbInfo = new FileInfo(_goalsDbPath);
            var walPath = _goalsDbPath + "-wal";
            var walInfo = File.Exists(walPath) ? new FileInfo(walPath) : null;
            var walWrite = walInfo?.LastWriteTimeUtc ?? DateTime.MinValue;
            var walLength = walInfo?.Length ?? -1;

            if (!force &&
                dbInfo.LastWriteTimeUtc <= _lastGoalsDbWriteUtc &&
                dbInfo.Length == _lastGoalsDbLength &&
                walWrite <= _lastGoalsWalWriteUtc &&
                walLength == _lastGoalsWalLength)
            {
                return;
            }

            var next = new Dictionary<string, GoalState>(StringComparer.OrdinalIgnoreCase);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _goalsDbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
            };

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT thread_id, objective, status, token_budget, tokens_used, time_used_seconds, created_at_ms, updated_at_ms
                FROM thread_goals
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var threadId = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(threadId))
                {
                    continue;
                }

                var objective = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var status = reader.IsDBNull(2) ? "" : reader.GetString(2);
                long? tokenBudget = reader.IsDBNull(3) ? null : reader.GetInt64(3);
                var tokensUsed = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                var timeUsedSeconds = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
                var createdAt = FromUnixMilliseconds(reader.GetInt64(6));
                var updatedAt = FromUnixMilliseconds(reader.GetInt64(7));

                next[threadId] = new GoalState(
                    threadId,
                    objective,
                    status,
                    tokenBudget,
                    tokensUsed,
                    TimeSpan.FromSeconds(Math.Max(0, timeUsedSeconds)),
                    createdAt,
                    updatedAt);
            }

            _goals.Clear();
            foreach (var pair in next)
            {
                _goals[pair.Key] = pair.Value;
            }

            _lastGoalsDbWriteUtc = dbInfo.LastWriteTimeUtc;
            _lastGoalsDbLength = dbInfo.Length;
            _lastGoalsWalWriteUtc = walWrite;
            _lastGoalsWalLength = walLength;
        }
        catch (Exception ex)
        {
            DebugLog("goals_load_failed", new { error = ex.Message });
        }
    }

    private void SyncGoalTasks()
    {
        foreach (var task in _tasks.Values)
        {
            task.Goal = _goals.GetValueOrDefault(task.ThreadId);
            if (task.Goal?.IsActive == true && task.Status == TaskVisualStatus.Done)
            {
                task.Status = TaskVisualStatus.Working;
                task.Message = "目标模式";
                task.CompletedAt = default;
                task.Duration = TimeSpan.Zero;
                task.WasUnread = false;
                task.UnreadClearedAt = null;
            }
        }

        var activeGoals = _goals.Values.Where(g => g.IsActive).ToList();
        foreach (var goal in activeGoals)
        {
            var goalTaskId = GoalTaskId(goal.ThreadId);
            if (!_tasks.TryGetValue(goalTaskId, out var task))
            {
                task = new TaskState(goalTaskId, goal.ThreadId, GetThreadTitle(goal.ThreadId), goal.CreatedAtUtc);
                _tasks[goalTaskId] = task;
                DebugLog("goal_task_added", new { goal.ThreadId, title = task.Title, goal.Status });
            }

            task.Goal = goal;
            task.ThreadId = goal.ThreadId;
            task.Title = GetThreadTitle(goal.ThreadId);
            task.StartedAt = goal.CreatedAtUtc;
            task.Status = TaskVisualStatus.Working;
            task.Message = "目标模式";
            task.LastEventAt = goal.UpdatedAtUtc;
        }

        var activeGoalTaskIds = activeGoals.Select(g => GoalTaskId(g.ThreadId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _tasks.Keys.Where(id => id.StartsWith("goal:", StringComparison.OrdinalIgnoreCase) && !activeGoalTaskIds.Contains(id)).ToList())
        {
            _tasks.Remove(id);
        }
    }

    private void ReconcileAcknowledgements()
    {
        var now = DateTime.UtcNow;
        foreach (var task in _tasks.Values)
        {
            if (task.Status == TaskVisualStatus.Done && _unreadThreadIds.Contains(task.ThreadId))
            {
                task.WasUnread = true;
                task.UnreadClearedAt = null;
            }
            else if (task.Status == TaskVisualStatus.Done && task.WasUnread && task.UnreadClearedAt is null)
            {
                task.UnreadClearedAt = DateTime.UtcNow;
            }
        }

        var acknowledged = _tasks.Values
            .Where(t => t.Status == TaskVisualStatus.Done &&
                        !_unreadThreadIds.Contains(t.ThreadId) &&
                        ((t.WasUnread &&
                          t.UnreadClearedAt is not null &&
                          (now - t.UnreadClearedAt.Value).TotalSeconds >= AcknowledgementDebounceSeconds) ||
                         (!t.WasUnread &&
                          IsValidCompletedAt(t.CompletedAt) &&
                          (now - t.CompletedAt).TotalSeconds >= ForegroundDoneGraceSeconds)))
            .Select(t => t.TurnId)
            .ToList();

        foreach (var id in acknowledged)
        {
            if (CountVisibleTasks() == 1)
            {
                DebugLog("ack_morph_idle", new { turnId = id });
                MorphTaskToIdle(id);
            }
            else
            {
                DebugLog("ack_remove", new { turnId = id });
                RemoveTask(id, exitUp: true);
            }
        }
    }

    private void AcknowledgeAllCompleted()
    {
        foreach (var id in _tasks.Values.Where(t => t.Status == TaskVisualStatus.Done).Select(t => t.TurnId).ToList())
        {
            if (CountVisibleTasks() == 1)
            {
                MorphTaskToIdle(id);
            }
            else
            {
                RemoveTask(id, exitUp: true);
            }
        }
    }

    private void DismissErrorTask(string turnId, string source)
    {
        if (!_tasks.TryGetValue(turnId, out var task) || task.Status != TaskVisualStatus.Error)
        {
            return;
        }

        DebugLog("manual_error_dismiss", new { turnId, threadId = task.ThreadId, source });
        if (_isCollapsed)
        {
            _tasks.Remove(turnId);
            Render();
            return;
        }

        RemoveTask(turnId, exitUp: false);
    }

    private void HardRefreshStatusLight()
    {
        DebugLog("manual_hard_refresh", new { collapsed = _isCollapsed, tasks = _tasks.Count, cards = _cards.Count });

        _modeTransitionTimer?.Stop();
        _modeTransitionTimer = null;
        _isModeTransitionRunning = false;

        foreach (var timer in _exitTimers.Values.ToList())
        {
            timer.Stop();
        }

        foreach (var card in _cards.Values.Distinct().ToList())
        {
            card.Root.BeginAnimation(OpacityProperty, null);
            card.Root.BeginAnimation(Canvas.TopProperty, null);
            card.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            Root.Children.Remove(card.Root);
        }

        if (_collapsedStrip is not null)
        {
            _collapsedStrip.BeginAnimation(OpacityProperty, null);
            Root.Children.Remove(_collapsedStrip);
            _collapsedStrip = null;
        }
        _collapsedSignature = null;
        _expandedRenderSignature = null;
        _collapsedWidth = double.NaN;

        _tasks.Clear();
        _cards.Clear();
        _exitTimers.Clear();
        _activeTurnsByFile.Clear();
        _exitingCards.Clear();
        _fileOffsets.Clear();
        _threadTitles.Clear();
        _goals.Clear();
        _seenEvents.Clear();
        _seenEventOrder.Clear();
        _completionSoundedTurns.Clear();
        _completionSoundedTurnOrder.Clear();
        foreach (var timer in _pendingCompletionSoundTimers.Values.ToList())
        {
            timer.Stop();
        }
        _pendingCompletionSoundTimers.Clear();
        _pendingCompletionSoundThreadIds.Clear();

        lock (_pendingFiles)
        {
            _pendingFiles.Clear();
        }

        _lastManualWriteUtc = DateTime.MinValue;
        _lastSessionIndexWriteUtc = DateTime.MinValue;
        _lastGlobalStateWriteUtc = DateTime.MinValue;
        _lastGoalsDbWriteUtc = DateTime.MinValue;
        _lastGoalsWalWriteUtc = DateTime.MinValue;
        _lastSessionWatcherEventUtc = DateTime.MinValue;
        _lastSessionIndexLength = -1;
        _lastGlobalStateLength = -1;
        _lastGoalsDbLength = -1;
        _lastGoalsWalLength = -1;
        _lastActiveFilePollUtc = DateTime.MinValue;
        _lastRecentFileScanUtc = DateTime.MinValue;

        LoadThreadTitles(force: true);
        LoadUnreadThreadIds(force: true);
        LoadGoals(force: true);
        BootstrapRecentSessions();
        Poll();
    }

    private int CountVisibleTasks()
    {
        return _tasks.Values.Count(t => t.Status is TaskVisualStatus.Working or TaskVisualStatus.Done or TaskVisualStatus.Input or TaskVisualStatus.Error) +
               _exitingCards.Count;
    }

    private void RemoveTask(string turnId, bool exitUp)
    {
        _tasks.Remove(turnId);
        DebugLog("remove_task", new { turnId, exitUp });
        if (_exitingCards.Contains(turnId))
        {
            return;
        }

        if (!_cards.TryGetValue(turnId, out var card))
        {
            return;
        }

        if (_isCollapsed || card.Root.Visibility != Visibility.Visible)
        {
            RemoveCardImmediately(turnId, card);
            Render();
            return;
        }

        _exitingCards.Add(turnId);
        var side = GetSlideSide();
        if (exitUp)
        {
            FlashCard(card, 0.90, 960, holdMilliseconds: 60);
        }

        Animate(card.Root, OpacityProperty, card.Root.Opacity, 0, 760, frameRate: SlideAnimationFrameRate);
        Animate(card.Translate, TranslateTransform.XProperty, card.Translate.X, side * 164, 900, frameRate: SlideAnimationFrameRate);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(980) };
        _exitTimers[turnId] = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Root.Children.Remove(card.Root);
            _cards.Remove(turnId);
            _exitingCards.Remove(turnId);
            _exitTimers.Remove(turnId);
            Render();
        };
        timer.Start();
    }

    private void RemoveCardImmediately(string turnId, TaskCard card)
    {
        if (_exitTimers.TryGetValue(turnId, out var timer))
        {
            timer.Stop();
            _exitTimers.Remove(turnId);
        }

        card.Root.BeginAnimation(OpacityProperty, null);
        card.Root.BeginAnimation(Canvas.TopProperty, null);
        card.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        Root.Children.Remove(card.Root);
        _cards.Remove(turnId);
        _exitingCards.Remove(turnId);
    }

    private void MorphTaskToIdle(string turnId)
    {
        if (_isQuotaPinned &&
            _cards.TryGetValue("idle", out var pinnedQuotaCard) &&
            _cards.TryGetValue(turnId, out var taskCard) &&
            !ReferenceEquals(pinnedQuotaCard, taskCard))
        {
            RemoveTask(turnId, exitUp: true);
            return;
        }

        _tasks.Remove(turnId);
        DebugLog("morph_idle", new { turnId });
        if (!_cards.TryGetValue(turnId, out var card))
        {
            Render();
            return;
        }

        if (_exitTimers.TryGetValue(turnId, out var exitTimer))
        {
            exitTimer.Stop();
            _exitTimers.Remove(turnId);
        }

        if (_cards.TryGetValue("idle", out var oldIdle) && !ReferenceEquals(oldIdle, card))
        {
            Root.Children.Remove(oldIdle.Root);
            _cards.Remove("idle");
        }

        _cards.Remove(turnId);
        _cards["idle"] = card;
        _exitingCards.Remove(turnId);

        card.Root.BeginAnimation(OpacityProperty, null);
        card.Root.BeginAnimation(Canvas.TopProperty, null);
        card.Translate.BeginAnimation(TranslateTransform.XProperty, null);

        var idle = CreateIdleState();
        UpdateCard(card, idle, 1, 1, suppressStatusFlash: false);
        if (double.IsNaN(Canvas.GetLeft(card.Root)))
        {
            Canvas.SetLeft(card.Root, PanelPadding);
        }

        if (double.IsNaN(Canvas.GetTop(card.Root)))
        {
            Canvas.SetTop(card.Root, PanelPadding);
        }

        card.Translate.X = 0;
        card.Root.Visibility = Visibility.Visible;
        card.Root.Opacity = 0.82;
        card.TargetY = Canvas.GetTop(card.Root);
    }

    private void TryMorphIdleCardToSingleTask(List<TaskState> visible)
    {
        if (_isCollapsed ||
            _isQuotaPinned ||
            visible.Count != 1 ||
            _cards.ContainsKey(visible[0].TurnId) ||
            !_cards.TryGetValue("idle", out var card) ||
            !Root.Children.Contains(card.Root) ||
            _exitingCards.Count > 0)
        {
            return;
        }

        var task = visible[0];
        if (task.Status is not (TaskVisualStatus.Working or TaskVisualStatus.Input))
        {
            return;
        }

        _cards.Remove("idle");
        _cards[task.TurnId] = card;
        _tasks.Remove("idle");
        _exitingCards.Remove("idle");

        card.Root.BeginAnimation(OpacityProperty, null);
        card.Root.BeginAnimation(Canvas.TopProperty, null);
        card.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        Canvas.SetLeft(card.Root, PanelPadding);
        Canvas.SetTop(card.Root, PanelPadding);
        card.TargetY = PanelPadding;
        card.Translate.X = 0;
        card.Root.Visibility = Visibility.Visible;
        card.Root.Opacity = 1;
        UpdateCard(card, task, 1, 1, suppressStatusFlash: false);
        DebugLog("morph_idle_to_task", new { turnId = task.TurnId, status = task.Status.ToString() });
    }

    private void Render()
    {
        if (_isModeTransitionRunning)
        {
            UpdatePaintTimerInterval();
            return;
        }

        var visible = GetVisibleTasks();
        TryMorphIdleCardToSingleTask(visible);
        var expandedRenderSignature = CreateExpandedRenderSignature(visible);
        if (CanSkipExpandedRender(visible, expandedRenderSignature))
        {
            RefreshVisibleDurations(visible);
            UpdatePaintTimerInterval();
            return;
        }
        _expandedRenderSignature = expandedRenderSignature;

        var ids = visible.Select(t => t.TurnId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _cards.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            if (id == "idle" || _exitingCards.Contains(id))
            {
                continue;
            }

            RemoveTask(id, exitUp: false);
        }

        if (_isCollapsed)
        {
            RenderCollapsed(visible);
            UpdatePaintTimerInterval();
            return;
        }

        if (_collapsedStrip is not null)
        {
            _collapsedStrip.BeginAnimation(OpacityProperty, null);
            Root.Children.Remove(_collapsedStrip);
            _collapsedStrip = null;
        }
        _collapsedSignature = null;
        _expandedRenderSignature = null;
        _collapsedWidth = double.NaN;

        var targetHeight = visible.Count == 0
            ? CardHeight + PanelPadding * 2
            : PanelPadding * 2 + visible.Count * CardHeight + Math.Max(0, visible.Count - 1) * Gap;
        foreach (var id in _exitingCards)
        {
            if (!_cards.TryGetValue(id, out var exitingCard) ||
                exitingCard.Root.Visibility != Visibility.Visible ||
                !Root.Children.Contains(exitingCard.Root))
            {
                continue;
            }

            var top = Canvas.GetTop(exitingCard.Root);
            if (double.IsNaN(top))
            {
                top = PanelPadding;
            }

            targetHeight = Math.Max(targetHeight, top + CardHeight + PanelPadding);
        }

        ResizeKeepingTop(targetHeight);

        for (var i = 0; i < visible.Count; i++)
        {
            var task = visible[i];
            var targetY = PanelPadding + i * (CardHeight + Gap);
            var isNewCard = false;
            if (!_cards.TryGetValue(task.TurnId, out var card))
            {
                isNewCard = true;
                card = CreateCard(task);
                _cards[task.TurnId] = card;
                Root.Children.Add(card.Root);
                Canvas.SetLeft(card.Root, PanelPadding);
                Canvas.SetTop(card.Root, targetY);
                card.TargetY = targetY;
                card.Root.Opacity = 0;
                var side = GetSlideSide();
                card.Translate.X = side * 164;
                Animate(card.Root, OpacityProperty, 0, 1, 560, frameRate: SlideAnimationFrameRate);
                Animate(card.Translate, TranslateTransform.XProperty, side * 164, 0, 760, frameRate: SlideAnimationFrameRate);
            }

            card.Root.Visibility = Visibility.Visible;
            if (card.Root.Opacity < 0.05)
            {
                card.Root.Opacity = 1;
            }

            UpdateCard(card, task, i + 1, visible.Count, suppressStatusFlash: isNewCard);
            var currentY = Canvas.GetTop(card.Root);
            if (double.IsNaN(currentY))
            {
                currentY = targetY;
            }

            if (double.IsNaN(card.TargetY) || Math.Abs(card.TargetY - targetY) > 0.5)
            {
                card.TargetY = targetY;
                Animate(card.Root, Canvas.TopProperty, currentY, targetY, 860);
            }
        }

        if (visible.Count == 0)
        {
            var anyVisibleExitingCard = _exitingCards.Any(id =>
                _cards.TryGetValue(id, out var exitingCard) &&
                exitingCard.Root.Visibility == Visibility.Visible &&
                exitingCard.Root.Opacity > 0.01);
            if (!anyVisibleExitingCard)
            {
                ShowIdleGhost();
            }
        }
        else
        {
            _tasks.Remove("idle");
            if (!ids.Contains("idle") &&
                _cards.TryGetValue("idle", out var idleCard) &&
                !_exitingCards.Contains("idle"))
            {
                if (!_isCollapsed &&
                    idleCard.Root.Visibility == Visibility.Visible &&
                    Root.Children.Contains(idleCard.Root))
                {
                    RemoveTask("idle", exitUp: false);
                }
                else
                {
                    Root.Children.Remove(idleCard.Root);
                    _cards.Remove("idle");
                }
            }
        }

        _expandedRenderSignature = CreateExpandedRenderSignature(visible);
        UpdatePaintTimerInterval();
    }

    private bool CanSkipExpandedRender(List<TaskState> visible, string signature)
    {
        if (_isCollapsed ||
            _isModeTransitionRunning ||
            _exitingCards.Count > 0 ||
            _collapsedStrip is not null ||
            !string.Equals(signature, _expandedRenderSignature, StringComparison.Ordinal))
        {
            return false;
        }

        if (visible.Count == 0)
        {
            return _cards.TryGetValue("idle", out var idleCard) &&
                   Root.Children.Contains(idleCard.Root) &&
                   idleCard.Root.Visibility == Visibility.Visible;
        }

        var visibleIds = visible.Select(t => t.TurnId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_cards.Keys.Any(id => !visibleIds.Contains(id) && !string.Equals(id, "idle", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        foreach (var task in visible)
        {
            if (!_cards.TryGetValue(task.TurnId, out var card) ||
                !Root.Children.Contains(card.Root) ||
                card.Root.Visibility != Visibility.Visible)
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshVisibleDurations(IEnumerable<TaskState> visible)
    {
        foreach (var task in visible)
        {
            if (_cards.TryGetValue(task.TurnId, out var card))
            {
                card.Duration.Text = FormatDuration(task);
            }
        }
    }

    private string CreateExpandedRenderSignature(List<TaskState> visible)
    {
        var builder = new StringBuilder();
        builder.Append(_isQuotaPinned).Append('|')
            .Append(_showQuotaPercentInRing).Append('|')
            .Append(_showExternalBalances).Append(':')
            .Append(_externalBalanceProviderCount).Append('|')
            .Append(_isCollapsed).Append('|')
            .Append(_cards.Count).Append('|');

        if (_rateLimits is not null)
        {
            builder.Append(Math.Round(_rateLimits.WeeklyRemainingPercent, 1)).Append(':')
                .Append(Math.Round(_rateLimits.FiveHourRemainingPercent, 1)).Append(':')
                .Append(_rateLimits.WeeklyResetsAt?.Ticks ?? 0).Append(':')
                .Append(_rateLimits.FiveHourResetsAt?.Ticks ?? 0);
        }

        foreach (var task in visible)
        {
            builder.Append('|')
                .Append(task.TurnId).Append(':')
                .Append(task.ThreadId).Append(':')
                .Append(task.Status).Append(':')
                .Append(task.Title).Append(':')
                .Append(task.Message).Append(':')
                .Append(task.StartedAt.Ticks).Append(':')
                .Append(task.CompletedAt.Ticks).Append(':')
                .Append(task.Duration.Ticks).Append(':')
                .Append(task.LastEventAt.Ticks).Append(':')
                .Append(task.Goal?.Status ?? "");
        }

        return builder.ToString();
    }

    private List<TaskState> GetVisibleTasks()
    {
        var visible = _tasks.Values
            .Where(t => t.Status is TaskVisualStatus.Working or TaskVisualStatus.Done or TaskVisualStatus.Input or TaskVisualStatus.Error)
            .GroupBy(GetDisplayIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(ChooseDisplayTaskForThread)
            .OrderBy(t => t.Goal?.IsActive == true ? 0 : 1)
            .ThenBy(t => t.Status == TaskVisualStatus.Done ? 0 : 1)
            .ThenByDescending(t => t.CompletedAt)
            .ThenByDescending(t => t.LastEventAt)
            .ThenByDescending(t => t.StartedAt)
            .Take(MaxVisibleTasks)
            .ToList();
        if (_isQuotaPinned)
        {
            visible.Insert(0, CreateIdleState());
            if (visible.Count > MaxVisibleTasks)
            {
                visible.RemoveAt(visible.Count - 1);
            }
        }

        return visible;
    }

    private static string GetDisplayIdentityKey(TaskState task)
    {
        var title = NormalizeTitle(task.Title);
        return string.IsNullOrWhiteSpace(title)
            ? task.ThreadId
            : title;
    }

    private static TaskState ChooseDisplayTaskForThread(IGrouping<string, TaskState> tasks)
    {
        return tasks
            .OrderBy(t => t.Goal?.IsActive == true ? 0 : 1)
            .ThenBy(t => t.TurnId.StartsWith("goal:", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t.Status == TaskVisualStatus.Working || t.Status == TaskVisualStatus.Input ? 0 : 1)
            .ThenByDescending(t => t.LastEventAt)
            .ThenByDescending(t => t.StartedAt)
            .First();
    }

    private void RenderCollapsed(List<TaskState> visible, bool animateEntrance = false)
    {
        var bulbStates = GetCollapsedBulbStates(visible);
        var stripWidth = GetCollapsedWidth(bulbStates.Count);
        var targetWidth = stripWidth + PanelPadding * 2;
        ResizeKeepingTop(CollapsedHeight + PanelPadding * 2, targetWidth, minWidth: targetWidth);

        DisposeExpandedCardsForCollapsed();

        var signature = CreateCollapsedSignature(bulbStates, stripWidth);
        if (_collapsedStrip is not null &&
            Root.Children.Contains(_collapsedStrip) &&
            !animateEntrance &&
            string.Equals(signature, _collapsedSignature, StringComparison.Ordinal) &&
            Math.Abs(stripWidth - _collapsedWidth) < 0.5)
        {
            return;
        }

        if (_collapsedStrip is not null)
        {
            _collapsedStrip.BeginAnimation(OpacityProperty, null);
            Root.Children.Remove(_collapsedStrip);
        }

        _collapsedStrip = CreateCollapsedStrip(bulbStates, stripWidth);
        _collapsedSignature = signature;
        _collapsedWidth = stripWidth;
        Root.Children.Add(_collapsedStrip);
        Canvas.SetLeft(_collapsedStrip, PanelPadding);
        Canvas.SetTop(_collapsedStrip, PanelPadding);
        if (animateEntrance)
        {
            var side = GetSlideSide();
            var startX = side * 108;
            var translate = new TranslateTransform(startX, 0);
            _collapsedStrip.RenderTransform = translate;
            _collapsedStrip.Opacity = 0;
            Animate(_collapsedStrip, OpacityProperty, 0, 1, 420, frameRate: SlideAnimationFrameRate);
            Animate(translate, TranslateTransform.XProperty, startX, 0, 560, frameRate: SlideAnimationFrameRate);
        }

        DebugLog("collapsed_strip_rebuild", new { bulbs = bulbStates.Count, rootChildren = Root.Children.Count, width = Math.Round(stripWidth, 1) });
    }

    private static List<TaskState> GetCollapsedBulbStates(IEnumerable<TaskState> visible)
    {
        var taskStates = visible
            .Where(task => !IsQuotaPageState(task))
            .Take(MaxVisibleTasks)
            .ToList();

        return taskStates.Count == 0
            ? new List<TaskState> { CreateIdleState() }
            : taskStates;
    }

    private static bool IsQuotaPageState(TaskState task)
    {
        return task.Status == TaskVisualStatus.Idle &&
               (string.Equals(task.TurnId, "idle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(task.ThreadId, "idle", StringComparison.OrdinalIgnoreCase));
    }

    private void DisposeExpandedCardsForCollapsed()
    {
        if (_cards.Count == 0 && _exitTimers.Count == 0 && _exitingCards.Count == 0)
        {
            return;
        }

        var disposedCount = _cards.Values.Distinct().Count();
        var exitingCount = _exitingCards.Count;

        foreach (var timer in _exitTimers.Values.ToList())
        {
            timer.Stop();
        }

        foreach (var card in _cards.Values.Distinct().ToList())
        {
            card.Root.BeginAnimation(OpacityProperty, null);
            card.Root.BeginAnimation(Canvas.TopProperty, null);
            card.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            Root.Children.Remove(card.Root);
        }

        _cards.Clear();
        _exitingCards.Clear();
        _exitTimers.Clear();
        _expandedRenderSignature = null;
        DebugLog("collapsed_dispose", new { disposed = disposedCount, exiting = exitingCount });
    }

    private static double GetCollapsedWidth(int bulbCount)
    {
        var count = Math.Max(1, Math.Min(MaxVisibleTasks, bulbCount));
        var bulbSpan = CollapsedBulbSize + Math.Max(0, count - 1) * CollapsedBulbSpacing;
        var naturalWidth = CollapsedBulbLeftMargin + bulbSpan + CollapsedBulbToToggleGap + ToggleButtonSize + PanelPadding;
        return naturalWidth;
    }

    private static string CreateCollapsedSignature(List<TaskState> states, double width)
    {
        var stateSignature = string.Join("|", states.Select(t => $"{GetDisplayIdentityKey(t)}:{t.Status}"));
        return width.ToString("F1", CultureInfo.InvariantCulture) + "|" + stateSignature;
    }

    private Border CreateCollapsedStrip(List<TaskState> states, double width)
    {
        var primaryPalette = GetPalette(states[0].Status);
        var strip = new Border
        {
            Width = width,
            Height = CollapsedHeight,
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
        };

        var shell = new Grid
        {
            Width = width,
            Height = CollapsedHeight,
            ClipToBounds = true,
        };

        shell.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(PanelCornerRadius),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
            Background = CreateGlassBrush(primaryPalette),
            IsHitTestVisible = false,
        });

        var edgeHighlights = new Grid
        {
            Width = width,
            Height = CollapsedHeight,
            IsHitTestVisible = false,
        };
        BuildEdgeHighlights(edgeHighlights, primaryPalette);
        shell.Children.Add(edgeHighlights);

        var canvas = new Canvas
        {
            Width = width,
            Height = CollapsedHeight,
            ClipToBounds = true,
        };
        shell.Children.Add(canvas);

        var bulbCount = Math.Max(1, Math.Min(MaxVisibleTasks, states.Count));
        var centerY = CollapsedHeight / 2;
        var toggleLeft = width - PanelPadding - ToggleButtonSize;
        var rightmostBulbLeft = toggleLeft - CollapsedBulbToToggleGap - CollapsedBulbSize;
        var leftmostBulbLeft = rightmostBulbLeft - Math.Max(0, bulbCount - 1) * CollapsedBulbSpacing;

        var wire = new Border
        {
            Width = Math.Max(12, (bulbCount - 1) * CollapsedBulbSpacing + CollapsedBulbSize),
            Height = 1.4,
            Background = CreateBulbWireBrush(primaryPalette),
            Opacity = bulbCount > 1 ? 0.76 : 0,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(wire, leftmostBulbLeft);
        Canvas.SetTop(wire, centerY - 0.7);
        canvas.Children.Add(wire);

        for (var i = 0; i < bulbCount; i++)
        {
            var task = states[i];
            var palette = GetPalette(task.Status);
            var bulb = CreateStatusBulb(palette, CollapsedBulbSize);
            if (task.Status == TaskVisualStatus.Error)
            {
                var turnId = task.TurnId;
                bulb.IsHitTestVisible = true;
                bulb.Cursor = Cursors.Hand;
                bulb.MouseLeftButtonDown += (_, e) => e.Handled = true;
                bulb.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    DismissErrorTask(turnId, "collapsed_bulb");
                };
            }

            Canvas.SetLeft(bulb, rightmostBulbLeft - i * CollapsedBulbSpacing);
            Canvas.SetTop(bulb, centerY - CollapsedBulbSize / 2);
            canvas.Children.Add(bulb);
        }

        var toggle = CreatePanelToggleButton(collapsed: true);
        Canvas.SetLeft(toggle, toggleLeft);
        Canvas.SetTop(toggle, ToggleButtonInset);
        canvas.Children.Add(toggle);

        shell.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(PanelCornerRadius - 1),
            BorderThickness = new Thickness(1.2),
            BorderBrush = CreateInnerRimBrush(),
            Margin = new Thickness(1),
            IsHitTestVisible = false,
        });

        var layers = new Grid
        {
            Width = width,
            Height = CollapsedHeight,
            ClipToBounds = false,
        };
        layers.Children.Add(new Border
        {
            Width = width,
            Height = CollapsedHeight,
            CornerRadius = new CornerRadius(PanelCornerRadius),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false,
            Opacity = 0,
        });
        layers.Children.Add(new Border
        {
            Width = width,
            Height = CollapsedHeight,
            CornerRadius = new CornerRadius(PanelCornerRadius),
            Background = CreateFrozenBrush(primaryPalette.Surface),
            BorderBrush = CreatePanelRimBrush(primaryPalette),
            BorderThickness = new Thickness(1),
            Child = shell,
        });

        strip.Child = layers;
        return strip;
    }

    private Border CreatePanelToggleButton(bool collapsed)
    {
        var palette = GetPalette(collapsed ? TaskVisualStatus.Working : TaskVisualStatus.Idle);
        var button = new Border
        {
            Width = ToggleButtonSize,
            Height = ToggleButtonSize,
            CornerRadius = new CornerRadius(8),
            Background = CreateToggleButtonBrush(palette),
            BorderBrush = CreateToggleButtonRimBrush(palette),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = CreateControlToolTip(collapsed ? Ui("展开", "Expand") : Ui("折叠", "Collapse")),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = palette.Accent,
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.26,
            },
        };

        var glyph = new TextBlock
        {
            Text = collapsed ? "+" : "-",
            FontFamily = FontForChinese(),
            FontWeight = FontWeights.Bold,
            FontSize = collapsed ? 16 : 18,
            Foreground = CreateFrozenBrush(Color.FromRgb(208, 215, 232)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, collapsed ? -1 : -4, 0, 0),
        };
        button.Child = glyph;
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleCollapsed();
        };
        return button;
    }

    private void ToggleCollapsed()
    {
        if (_isModeTransitionRunning)
        {
            return;
        }

        if (!_isCollapsed)
        {
            BeginCollapseTransition();
            return;
        }

        BeginExpandTransition();
    }

    private void BeginCollapseTransition()
    {
        DebugLog("toggle_collapsed", new { collapsed = true, animated = true });

        var cards = _cards.Values
            .Distinct()
            .Where(card => card.Root.Visibility == Visibility.Visible && Root.Children.Contains(card.Root))
            .ToList();

        if (cards.Count == 0)
        {
            _isCollapsed = true;
            RenderCollapsed(GetVisibleTasks(), animateEntrance: true);
            UpdatePaintTimerInterval();
            return;
        }

        _isModeTransitionRunning = true;
        _modeTransitionTimer?.Stop();
        _modeTransitionTimer = null;

        foreach (var timer in _exitTimers.Values.ToList())
        {
            timer.Stop();
        }
        _exitTimers.Clear();
        _exitingCards.Clear();

        var side = GetSlideSide();
        var exitX = side * (CardWidth + 52.0);
        foreach (var card in cards)
        {
            card.Root.BeginAnimation(Canvas.TopProperty, null);
            AnimateModeExit(card.Root, card.Translate, exitX, 860, fadeDelayMilliseconds: 760, fadeMilliseconds: 190);
        }

        var timerFinish = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _modeTransitionTimer = timerFinish;
        timerFinish.Tick += (_, _) =>
        {
            timerFinish.Stop();
            if (ReferenceEquals(_modeTransitionTimer, timerFinish))
            {
                _modeTransitionTimer = null;
            }

            _isCollapsed = true;
            _isModeTransitionRunning = false;
            _collapsedSignature = null;
            _expandedRenderSignature = null;
            _collapsedWidth = double.NaN;
            RenderCollapsed(GetVisibleTasks(), animateEntrance: true);
            UpdatePaintTimerInterval();
        };
        timerFinish.Start();
    }

    private void BeginExpandTransition()
    {
        DebugLog("toggle_collapsed", new { collapsed = false, animated = true });

        _isModeTransitionRunning = true;
        _modeTransitionTimer?.Stop();
        _modeTransitionTimer = null;

        foreach (var timer in _exitTimers.Values.ToList())
        {
            timer.Stop();
        }
        _exitTimers.Clear();
        _exitingCards.Clear();

        if (_collapsedStrip is null || !Root.Children.Contains(_collapsedStrip))
        {
            _isCollapsed = false;
            _isModeTransitionRunning = false;
            _collapsedSignature = null;
            _expandedRenderSignature = null;
            _collapsedWidth = double.NaN;
            Render();
            UpdatePaintTimerInterval();
            return;
        }

        var visible = GetVisibleTasks();
        var bulbStates = GetCollapsedBulbStates(visible);
        var stripWidth = GetCollapsedWidth(bulbStates.Count);
        var side = GetSlideSide();
        var oldCollapsedStrip = _collapsedStrip;
        oldCollapsedStrip.BeginAnimation(OpacityProperty, null);
        if (oldCollapsedStrip.RenderTransform is TranslateTransform oldTranslate)
        {
            oldTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        }

        Root.Children.Remove(oldCollapsedStrip);
        _collapsedStrip = null;

        var expandedCardCount = Math.Max(1, visible.Count);
        var expandedHeight = PanelPadding * 2 + expandedCardCount * CardHeight + Math.Max(0, expandedCardCount - 1) * Gap;
        ResizeKeepingTop(expandedHeight);

        _collapsedStrip = CreateCollapsedStrip(bulbStates, stripWidth);
        _collapsedSignature = CreateCollapsedSignature(bulbStates, stripWidth);
        _collapsedWidth = stripWidth;
        _collapsedStrip.IsHitTestVisible = false;
        Root.Children.Add(_collapsedStrip);
        Canvas.SetLeft(_collapsedStrip, side < 0 ? PanelPadding : GetLogicalWindowWidth() - PanelPadding - stripWidth);
        Canvas.SetTop(_collapsedStrip, PanelPadding);
        Panel.SetZIndex(_collapsedStrip, 20);
        _collapsedStrip.Visibility = Visibility.Visible;
        _collapsedStrip.Opacity = 1;
        var preparedCards = PrepareExpandedCardsForExpandTransition(visible);

        var translate = _collapsedStrip.RenderTransform as TranslateTransform;
        if (translate is null)
        {
            translate = new TranslateTransform(0, 0);
            _collapsedStrip.RenderTransform = translate;
        }
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.X = 0;

        DebugLog("expand_transition_strip", new { side, left = Math.Round(Canvas.GetLeft(_collapsedStrip), 1), width = Math.Round(stripWidth, 1), windowWidth = Math.Round(Width, 1) });
        AnimateModeExit(_collapsedStrip, translate, side * 108, 560);

        var stripExitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(640) };
        _modeTransitionTimer = stripExitTimer;
        stripExitTimer.Tick += (_, _) =>
        {
            stripExitTimer.Stop();
            if (!ReferenceEquals(_modeTransitionTimer, stripExitTimer))
            {
                return;
            }

            if (_collapsedStrip is not null)
            {
                _collapsedStrip.BeginAnimation(OpacityProperty, null);
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                Root.Children.Remove(_collapsedStrip);
                _collapsedStrip = null;
            }

            _isCollapsed = false;
            _collapsedSignature = null;
            _expandedRenderSignature = null;
            _collapsedWidth = double.NaN;

            StartExpandedCardsEntrance(preparedCards);
            UpdatePaintTimerInterval();

            var finishTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(840) };
            _modeTransitionTimer = finishTimer;
            finishTimer.Tick += (_, _) =>
            {
                finishTimer.Stop();
                if (ReferenceEquals(_modeTransitionTimer, finishTimer))
                {
                    _modeTransitionTimer = null;
                }

                foreach (var card in preparedCards.Where(card => Root.Children.Contains(card.Root)))
                {
                    card.Root.BeginAnimation(OpacityProperty, null);
                    card.Translate.BeginAnimation(TranslateTransform.XProperty, null);
                    card.Root.Opacity = card.Status == TaskVisualStatus.Idle ? 0.82 : 1;
                    card.Translate.X = 0;
                }

                _isModeTransitionRunning = false;
                Render();
                UpdatePaintTimerInterval();
            };
            finishTimer.Start();
        };
        stripExitTimer.Start();
    }

    private List<TaskCard> PrepareExpandedCardsForExpandTransition(List<TaskState> visible)
    {
        var tasksForCards = visible.Count == 0 ? new List<TaskState> { CreateIdleState() } : visible;
        var ids = tasksForCards.Select(t => t.TurnId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _cards.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            RemoveCardImmediately(id, _cards[id]);
        }

        var side = GetSlideSide();
        var preparedCards = new List<TaskCard>();
        for (var i = 0; i < tasksForCards.Count; i++)
        {
            var task = tasksForCards[i];
            var targetY = PanelPadding + i * (CardHeight + Gap);
            if (!_cards.TryGetValue(task.TurnId, out var card))
            {
                card = CreateCard(task);
                _cards[task.TurnId] = card;
                Root.Children.Add(card.Root);
            }
            else if (!Root.Children.Contains(card.Root))
            {
                Root.Children.Add(card.Root);
            }

            card.Root.BeginAnimation(OpacityProperty, null);
            card.Root.BeginAnimation(Canvas.TopProperty, null);
            card.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            Canvas.SetLeft(card.Root, PanelPadding);
            Canvas.SetTop(card.Root, targetY);
            Panel.SetZIndex(card.Root, 0);
            card.TargetY = targetY;
            card.Root.Visibility = Visibility.Visible;
            card.Root.Opacity = 0;
            card.Translate.X = side * 164;
            UpdateCard(card, task, i + 1, tasksForCards.Count, suppressStatusFlash: true);
            preparedCards.Add(card);
        }

        Root.UpdateLayout();
        DebugLog("expand_prepare_cards", new { cards = preparedCards.Count, rootChildren = Root.Children.Count });
        return preparedCards;
    }

    private void StartExpandedCardsEntrance(List<TaskCard> cards)
    {
        var side = GetSlideSide();
        foreach (var card in cards.Where(card => Root.Children.Contains(card.Root)))
        {
            card.Root.BeginAnimation(OpacityProperty, null);
            card.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            card.Root.Visibility = Visibility.Visible;
            card.Root.Opacity = 0;
            card.Translate.X = side * 164;
            Animate(card.Root, OpacityProperty, 0, card.Status == TaskVisualStatus.Idle ? 0.82 : 1, 560, frameRate: SlideAnimationFrameRate);
            Animate(card.Translate, TranslateTransform.XProperty, side * 164, 0, 760, frameRate: SlideAnimationFrameRate);
        }
    }

    private void AnimateModeExit(UIElement element, TranslateTransform translate, double exitX, int translateMilliseconds, int fadeDelayMilliseconds = 200, int? fadeMilliseconds = null)
    {
        element.BeginAnimation(OpacityProperty, null);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        var opacityMilliseconds = fadeMilliseconds ?? Math.Max(240, translateMilliseconds - fadeDelayMilliseconds);
        Animate(element, OpacityProperty, element.Opacity, 0, opacityMilliseconds, delayMilliseconds: fadeDelayMilliseconds, frameRate: SlideAnimationFrameRate);
        Animate(translate, TranslateTransform.XProperty, translate.X, exitX, translateMilliseconds, easingMode: EasingMode.EaseOut, frameRate: SlideAnimationFrameRate);
    }

    private void UpdatePaintTimerInterval()
    {
        if (Visibility != Visibility.Visible || _isCollapsed)
        {
            if (_paintTimer.IsEnabled)
            {
                _paintTimer.Stop();
            }

            if (Math.Abs(_pollTimer.Interval.TotalMilliseconds - 1000) > 0.5)
            {
                _pollTimer.Interval = TimeSpan.FromMilliseconds(1000);
            }

            return;
        }

        if (!_paintTimer.IsEnabled)
        {
            _paintTimer.Start();
        }

        if (Math.Abs(_pollTimer.Interval.TotalMilliseconds - 250) > 0.5)
        {
            _pollTimer.Interval = TimeSpan.FromMilliseconds(250);
        }

        var visibleStatuses = _cards
            .Where(kvp => !_exitingCards.Contains(kvp.Key))
            .Select(kvp => kvp.Value.Status)
            .ToList();

        var targetMs = visibleStatuses.Any(s => s is TaskVisualStatus.Working or TaskVisualStatus.Input or TaskVisualStatus.Error)
            ? 60
            : visibleStatuses.Any(s => s == TaskVisualStatus.Done)
                ? 90
                : 180;

        if (Math.Abs(_paintTimer.Interval.TotalMilliseconds - targetMs) > 0.5)
        {
            _paintTimer.Interval = TimeSpan.FromMilliseconds(targetMs);
        }
    }

    private void ShowIdleGhost()
    {
        var idle = CreateIdleState();

        if (!_cards.TryGetValue("idle", out var card))
        {
            card = CreateCard(idle);
            _cards["idle"] = card;
            Root.Children.Add(card.Root);
            Canvas.SetLeft(card.Root, PanelPadding);
            Canvas.SetTop(card.Root, PanelPadding);
            card.Root.Opacity = 0.82;
        }

        if (!Root.Children.Contains(card.Root))
        {
            Root.Children.Add(card.Root);
        }

        card.Root.Visibility = Visibility.Visible;
        card.Root.Opacity = 0.82;
        card.Translate.X = 0;
        Canvas.SetLeft(card.Root, PanelPadding);
        Canvas.SetTop(card.Root, PanelPadding);
        card.ToggleButton.Visibility = Visibility.Visible;
        UpdateCard(card, idle, 1, 1, suppressStatusFlash: true);
    }

    private static TaskState CreateIdleState()
    {
        return new TaskState("idle", "idle", "额度页", DateTime.UtcNow)
        {
            Status = TaskVisualStatus.Idle,
            Message = "额度页"
        };
    }

    private TaskCard CreateCard(TaskState task)
    {
        var palette = GetPalette(task.Status);
        var translate = new TranslateTransform();
        var border = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            Background = Brushes.Transparent,
            RenderTransform = translate,
            SnapsToDevicePixels = true,
            Tag = task.TurnId,
        };

        var cardLayers = new Grid
        {
            Width = CardWidth,
            Height = CardHeight,
            ClipToBounds = false,
        };

        var surfaceGlow = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            CornerRadius = new CornerRadius(PanelCornerRadius),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false,
            Opacity = 0,
        };
        cardLayers.Children.Add(surfaceGlow);

        var surface = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            CornerRadius = new CornerRadius(PanelCornerRadius),
            BorderThickness = new Thickness(1),
            Background = CreateFrozenBrush(palette.Surface),
            BorderBrush = CreatePanelRimBrush(palette),
            SnapsToDevicePixels = true,
        };
        cardLayers.Children.Add(surface);

        var shell = new Grid
        {
            Width = CardWidth,
            Height = CardHeight,
            ClipToBounds = false,
        };
        var clippedBackdrop = new Grid
        {
            Width = CardWidth,
            Height = CardHeight,
            ClipToBounds = false,
            IsHitTestVisible = false,
        };
        clippedBackdrop.Loaded += (_, _) =>
        {
            clippedBackdrop.Clip = new RectangleGeometry(new Rect(0, 0, CardWidth, CardHeight), PanelCornerRadius, PanelCornerRadius);
        };

        var pixels = new Canvas
        {
            Width = CardWidth,
            Height = CardHeight,
            IsHitTestVisible = false,
        };
        clippedBackdrop.Children.Add(pixels);

        var glass = new Border
        {
            CornerRadius = new CornerRadius(PanelCornerRadius),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
            Background = CreateGlassBrush(palette),
            IsHitTestVisible = false,
        };
        clippedBackdrop.Children.Add(glass);

        var edgeHighlights = new Grid
        {
            Width = CardWidth,
            Height = CardHeight,
            IsHitTestVisible = false,
        };
        BuildEdgeHighlights(edgeHighlights, palette);
        clippedBackdrop.Children.Add(edgeHighlights);

        var innerRim = new Border
        {
            CornerRadius = new CornerRadius(19),
            BorderThickness = new Thickness(1.2),
            BorderBrush = CreateInnerRimBrush(),
            Margin = new Thickness(1),
            IsHitTestVisible = false,
        };
        clippedBackdrop.Children.Add(innerRim);

        var flash = new Border
        {
            CornerRadius = new CornerRadius(PanelCornerRadius),
            Background = CreateFlashBrush(),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        clippedBackdrop.Children.Add(flash);
        shell.Children.Add(clippedBackdrop);

        var grid = new Grid
        {
            Margin = new Thickness(16, 11, 16, 9),
            ClipToBounds = false,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ClipToBounds = false,
        };
        Grid.SetColumn(textStack, 0);
        grid.Children.Add(textStack);

        var title = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(236, 240, 248)),
            FontFamily = FontForChinese(),
            FontWeight = FontWeights.SemiBold,
            FontSize = 18.2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        textStack.Children.Add(title);

        var message = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(184, 190, 204)),
            FontFamily = FontForChinese(),
            FontWeight = FontWeights.Normal,
            FontSize = 12.8,
            Margin = new Thickness(0, 5, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
        };

        var metaRow = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 9, 0, 0),
            MinHeight = 40,
            ClipToBounds = false,
        };
        metaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        metaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        textStack.Children.Add(metaRow);

        const double badgeHeight = 36;
        const double badgeRadius = 16;
        const double badgeRimThickness = 1.75;
        var badge = new Border
        {
            CornerRadius = new CornerRadius(badgeRadius),
            Height = badgeHeight,
            MinWidth = 92,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        var badgeLayers = new Grid
        {
            Height = badgeHeight,
            ClipToBounds = false,
            SnapsToDevicePixels = false,
        };
        var badgeFill = new Border
        {
            CornerRadius = new CornerRadius(badgeRadius),
            Background = CreateSoftBadgeBrush(palette),
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false,
            SnapsToDevicePixels = false,
        };
        var badgeRim = new ShapePath
        {
            StrokeThickness = 0,
            Stroke = null,
            Fill = CreateBadgeRimBrush(palette),
            Stretch = Stretch.None,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            SnapsToDevicePixels = false,
        };
        badgeLayers.SizeChanged += (_, _) =>
        {
            UpdateBadgeRimGeometry(badgeRim, badgeLayers.ActualWidth, badgeLayers.ActualHeight, badgeRimThickness, badgeRadius);
        };
        var badgeTextHost = new Border
        {
            Padding = new Thickness(12, 6, 12, 7),
            Height = badgeHeight,
            Child = null,
        };
        var badgeText = new TextBlock
        {
            FontFamily = FontForChinese(),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.8,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };
        badgeTextHost.Child = badgeText;
        badgeLayers.Children.Add(badgeFill);
        badgeLayers.Children.Add(badgeTextHost);
        badgeLayers.Children.Add(badgeRim);
        badge.Child = badgeLayers;
        badge.Tag = new BadgeLayers(badgeFill, badgeRim);
        Grid.SetColumn(badge, 0);
        Panel.SetZIndex(badge, 2);
        metaRow.Children.Add(badge);

        var durationHost = new Grid
        {
            MinWidth = 112,
            Height = 33,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = false,
        };
        var durationTrail = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
        };
        var durationAura = new Border
        {
            Height = 31,
            Margin = new Thickness(-3, 0, -6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(10),
            Background = CreateDurationTrailAuraBrush(palette),
            OpacityMask = CreateDurationTrailMask(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = palette.Accent,
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.24,
            },
        };
        var durationCore = new Border
        {
            Height = 20,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(4),
            Background = CreateDurationTrailCoreBrush(palette),
            OpacityMask = CreateDurationTrailMask(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = palette.Highlight,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.22,
            },
        };
        durationTrail.Children.Add(durationAura);
        durationTrail.Children.Add(durationCore);
        durationHost.Children.Add(durationTrail);

        var duration = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(202, 211, 229)),
            FontFamily = FontForDuration(),
            FontWeight = FontWeights.Black,
            FontSize = 21.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(12, -1, 12, 1),
        };
        durationHost.Children.Add(duration);
        Grid.SetColumn(durationHost, 1);
        Panel.SetZIndex(durationHost, 1);
        metaRow.Children.Add(durationHost);

        shell.Children.Add(grid);

        var quotaPanel = CreateQuotaPanel(
            out var weeklyQuotaArc,
            out var weeklyQuotaText,
            out var fiveHourQuotaArc,
            out var fiveHourQuotaText,
            out var shengshengBalanceText,
            out var shengshengBalanceArrowText,
            out var shengshengBalanceDeltaText,
            out var deepkeyBalanceText,
            out var deepkeyBalanceArrowText,
            out var deepkeyBalanceDeltaText);
        shell.Children.Add(quotaPanel);

        var quotaPinButton = CreateQuotaPinButton(out var quotaPinHead, out var quotaPinNeedle);
        quotaPinButton.HorizontalAlignment = HorizontalAlignment.Right;
        quotaPinButton.VerticalAlignment = VerticalAlignment.Top;
        quotaPinButton.Margin = new Thickness(0, ToggleButtonInset, ToggleButtonInset + ToggleButtonSize + 6, 0);
        shell.Children.Add(quotaPinButton);

        var quotaResetButton = CreateExternalBalanceResetButton();
        quotaResetButton.HorizontalAlignment = HorizontalAlignment.Right;
        quotaResetButton.VerticalAlignment = VerticalAlignment.Top;
        quotaResetButton.Margin = new Thickness(0, ToggleButtonInset, ToggleButtonInset + (ToggleButtonSize + 6) * 2, 0);
        shell.Children.Add(quotaResetButton);

        var collapseButton = CreatePanelToggleButton(collapsed: false);
        collapseButton.HorizontalAlignment = HorizontalAlignment.Right;
        collapseButton.VerticalAlignment = VerticalAlignment.Top;
        collapseButton.Margin = new Thickness(0, ToggleButtonInset, ToggleButtonInset, 0);
        shell.Children.Add(collapseButton);

        cardLayers.Children.Add(shell);
        border.Child = cardLayers;
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (border.Tag is not string id || !_tasks.TryGetValue(id, out var liveTask))
            {
                return;
            }

            if (!_isCollapsed && e.ClickCount == 1 && liveTask.Status == TaskVisualStatus.Error)
            {
                DismissErrorTask(id, "expanded_card");
                e.Handled = true;
                return;
            }

            if (e.ClickCount == 2 && liveTask.Status == TaskVisualStatus.Done)
            {
                if (CountVisibleTasks() == 1)
                {
                    MorphTaskToIdle(id);
                }
                else
                {
                    RemoveTask(id, exitUp: true);
                }
                e.Handled = true;
            }
        };

        var card = new TaskCard(border, surface, surfaceGlow, translate, pixels, glass, edgeHighlights, flash, grid, quotaPanel, weeklyQuotaArc, weeklyQuotaText, fiveHourQuotaArc, fiveHourQuotaText, shengshengBalanceText, shengshengBalanceArrowText, shengshengBalanceDeltaText, deepkeyBalanceText, deepkeyBalanceArrowText, deepkeyBalanceDeltaText, quotaPinButton, quotaResetButton, quotaPinHead, quotaPinNeedle, title, message, badge, badgeText, durationTrail, durationAura, durationCore, duration, collapseButton);
        BuildPixels(card, palette, task.Status);
        return card;
    }

    private Border CreateQuotaPinButton(out ShapePath pinHead, out ShapePath pinNeedle)
    {
        var palette = GetPalette(TaskVisualStatus.Idle);
        var button = new Border
        {
            Width = ToggleButtonSize,
            Height = ToggleButtonSize,
            CornerRadius = new CornerRadius(8),
            Background = CreateToggleButtonBrush(palette),
            BorderBrush = CreateToggleButtonRimBrush(palette),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = CreateControlToolTip(Ui("固定", "Pin")),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(168, 176, 190),
                BlurRadius = 9,
                ShadowDepth = 0,
                Opacity = 0.18,
            },
        };

        var icon = new Canvas
        {
            Width = 17,
            Height = 17,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        pinHead = new ShapePath
        {
            Data = Geometry.Parse("M15.113,3.21 L15.207,3.293 L20.707,8.793 A1,1 0 0 1 19.532,10.383 L16.36,13.554 L14.936,17.351 A1,1 0 0 1 14.778,17.628 L14.708,17.708 L13.208,19.208 A1,1 0 0 1 11.888,19.29 L11.793,19.207 L9,16.415 L5.207,20.207 A1,1 0 0 1 3.71,18.887 L3.793,18.793 L7.585,15 L4.793,12.207 A1,1 0 0 1 4.71,10.887 L4.793,10.793 L6.293,9.293 A1,1 0 0 1 6.551,9.106 L6.649,9.064 L10.445,7.639 L13.616,4.469 A1,1 0 0 1 15.113,3.21 Z"),
            StrokeThickness = 0,
            Fill = Brushes.Transparent,
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(0.74, 0.74),
                    new TranslateTransform(-0.35, -0.35),
                }
            },
            RenderTransformOrigin = new Point(0, 0),
        };
        icon.Children.Add(pinHead);

        pinNeedle = new ShapePath
        {
            Data = Geometry.Empty,
            StrokeThickness = 0,
            IsHitTestVisible = false,
        };
        icon.Children.Add(pinNeedle);

        button.Child = icon;
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleQuotaPinned();
        };
        return button;
    }

    private Grid CreateQuotaPanel(
        out ShapePath weeklyArc,
        out TextBlock weeklyText,
        out ShapePath fiveHourArc,
        out TextBlock fiveHourText,
        out TextBlock shengshengBalanceText,
        out TextBlock shengshengBalanceArrowText,
        out TextBlock shengshengBalanceDeltaText,
        out TextBlock deepkeyBalanceText,
        out TextBlock deepkeyBalanceArrowText,
        out TextBlock deepkeyBalanceDeltaText)
    {
        var panel = new Grid
        {
            Width = _showExternalBalances ? QuotaPanelWidth : QuotaOnlyPanelWidth,
            Margin = _showExternalBalances ? new Thickness(12, 5, 0, 5) : new Thickness(0, 5, 0, 5),
            HorizontalAlignment = _showExternalBalances ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = true,
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_showExternalBalances ? 142 : QuotaOnlyPanelWidth) });
        if (_showExternalBalances)
        {
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(156) });
        }

        var quotaOnlyLayout = !_showExternalBalances;
        Panel rings = quotaOnlyLayout
            ? new Grid
            {
                Width = QuotaOnlyPanelWidth,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(31) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            }
            : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };

        var weeklyRing = CreateQuotaRing(
            Color.FromRgb(219, 225, 232),
            Color.FromArgb(48, 226, 232, 240),
            out weeklyArc,
            out weeklyText,
            quotaOnlyLayout ? QuotaOnlyRingSize : QuotaRingSize,
            quotaOnlyLayout ? QuotaOnlyRingStroke : QuotaRingStroke);
        weeklyRing.Margin = quotaOnlyLayout ? new Thickness(0) : new Thickness(0, 0, 2, 0);
        rings.Children.Add(weeklyRing);

        var fiveHourRing = CreateQuotaRing(
            Color.FromRgb(158, 215, 174),
            Color.FromArgb(42, 177, 226, 191),
            out fiveHourArc,
            out fiveHourText,
            quotaOnlyLayout ? QuotaOnlyRingSize : QuotaRingSize,
            quotaOnlyLayout ? QuotaOnlyRingStroke : QuotaRingStroke);
        fiveHourRing.Margin = quotaOnlyLayout ? new Thickness(0) : new Thickness(2, 0, 0, 0);
        if (quotaOnlyLayout)
        {
            Grid.SetColumn(weeklyRing, 0);
            Grid.SetColumn(fiveHourRing, 2);
        }
        rings.Children.Add(fiveHourRing);
        Grid.SetColumn(rings, 0);
        panel.Children.Add(rings);

        shengshengBalanceText = CreateExternalBalanceValueText();
        shengshengBalanceArrowText = CreateExternalBalanceArrowText();
        shengshengBalanceDeltaText = CreateExternalBalanceDeltaText();
        deepkeyBalanceText = CreateExternalBalanceValueText();
        deepkeyBalanceArrowText = CreateExternalBalanceArrowText();
        deepkeyBalanceDeltaText = CreateExternalBalanceDeltaText();
        if (_showExternalBalances)
        {
            var isSingleProvider = _externalBalanceProviderCount == 1;
            var balanceStack = new StackPanel
            {
                Margin = isSingleProvider ? new Thickness(0, 20, 0, 0) : new Thickness(0, 26, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            balanceStack.Children.Add(CreateExternalBalancePlate(
                shengshengBalanceText,
                shengshengBalanceArrowText,
                shengshengBalanceDeltaText,
                TaskVisualStatus.Done,
                isSingleProvider ? 36 : 26));
            if (!isSingleProvider)
            {
                balanceStack.Children.Add(CreateExternalBalancePlate(deepkeyBalanceText, deepkeyBalanceArrowText, deepkeyBalanceDeltaText, TaskVisualStatus.Working));
            }
            Grid.SetColumn(balanceStack, 2);
            panel.Children.Add(balanceStack);
        }

        return panel;
    }

    private TextBlock CreateExternalBalanceValueText()
    {
        return new TextBlock
        {
            FontFamily = FontForDuration(),
            FontWeight = FontWeights.Black,
            FontSize = 15.8,
            Foreground = CreateFrozenBrush(Color.FromRgb(230, 236, 247)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private TextBlock CreateExternalBalanceArrowText()
    {
        return new TextBlock
        {
            FontFamily = FontForChinese(),
            FontWeight = FontWeights.Black,
            FontSize = 10.8,
            Foreground = CreateFrozenBrush(Color.FromRgb(134, 224, 157)),
            Text = "",
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 2, 0),
            RenderTransform = new TranslateTransform(0, -3.4),
        };
    }

    private TextBlock CreateExternalBalanceDeltaText()
    {
        return new TextBlock
        {
            FontFamily = FontForDuration(),
            FontWeight = FontWeights.Black,
            FontSize = 15.8,
            Foreground = CreateFrozenBrush(Color.FromRgb(134, 224, 157)),
            Text = "",
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private Border CreateExternalBalancePlate(TextBlock valueText, TextBlock arrowText, TextBlock deltaText, TaskVisualStatus tint, double height = 26)
    {
        var palette = GetPalette(tint);
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        Grid.SetColumn(valueText, 0);
        Grid.SetColumn(arrowText, 1);
        Grid.SetColumn(deltaText, 2);
        content.Children.Add(valueText);
        content.Children.Add(arrowText);
        content.Children.Add(deltaText);

        return new Border
        {
            Height = height,
            Margin = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 0, 5, 1),
            Background = CreateExternalBalancePlateBrush(palette),
            BorderBrush = CreateExternalBalancePlateRimBrush(palette),
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private Border CreateExternalBalanceResetButton()
    {
        var palette = GetPalette(TaskVisualStatus.Idle);
        var icon = new Canvas
        {
            Width = 15,
            Height = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.Children.Add(new ShapePath
        {
            Data = Geometry.Parse("M8,4 L4,8 L8,12 M4,8 H13 C16,8 18,10 18,13 C18,16 16,18 13,18 H9"),
            Stroke = CreateFrozenBrush(Color.FromRgb(246, 248, 255)),
            StrokeThickness = 1.82,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(0.72, 0.72),
                    new TranslateTransform(-0.9, -0.9),
                }
            },
            RenderTransformOrigin = new Point(0, 0),
        });

        var button = new Border
        {
            Width = ToggleButtonSize,
            Height = ToggleButtonSize,
            CornerRadius = new CornerRadius(8),
            Background = CreateToggleButtonBrush(palette),
            BorderBrush = CreateToggleButtonRimBrush(palette),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = icon,
            ToolTip = CreateControlToolTip(Ui("设置归零水平面", "Set baseline")),
            Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(168, 176, 190),
                BlurRadius = 9,
                ShadowDepth = 0,
                Opacity = 0.18,
            },
        };
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            SetExternalBalanceBaseline();
        };
        return button;
    }

    private Grid CreateQuotaRing(Color accent, Color track, out ShapePath arc, out TextBlock text, double ringSize = QuotaRingSize, double ringStroke = QuotaRingStroke)
    {
        var ring = new Grid
        {
            Width = ringSize + 7,
            Height = ringSize + 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var ringHost = new Grid
        {
            Width = ringSize,
            Height = ringSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var trackRing = new Ellipse
        {
            Width = ringSize,
            Height = ringSize,
            StrokeThickness = ringStroke,
            Stroke = CreateFrozenBrush(track),
        };
        ToolTipService.SetInitialShowDelay(trackRing, 120);
        ToolTipService.SetShowDuration(trackRing, 60000);
        ringHost.Children.Add(trackRing);

        arc = new ShapePath
        {
            StrokeThickness = ringStroke,
            Stroke = CreateFrozenBrush(accent),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Empty,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accent,
                BlurRadius = 9,
                ShadowDepth = 0,
                Opacity = 0.22,
            }
        };
        ToolTipService.SetInitialShowDelay(arc, 120);
        ToolTipService.SetShowDuration(arc, 60000);
        ringHost.Children.Add(arc);

        var centerGlass = new Ellipse
        {
            Width = ringSize - 8,
            Height = ringSize - 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = CreateQuotaCenterGlassBrush(accent),
            Stroke = CreateFrozenBrush(Color.FromArgb(82, 255, 255, 255)),
            StrokeThickness = 1.45,
            IsHitTestVisible = true,
        };
        ToolTipService.SetInitialShowDelay(centerGlass, 120);
        ToolTipService.SetShowDuration(centerGlass, 60000);
        ringHost.Children.Add(centerGlass);

        text = new TextBlock
        {
            Foreground = CreateFrozenBrush(accent),
            FontFamily = FontForDuration(),
            FontWeight = FontWeights.Black,
            FontSize = 19.2 * ringSize / QuotaRingSize,
            Width = ringSize - 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Text = "--/--",
        };
        ToolTipService.SetInitialShowDelay(text, 120);
        ToolTipService.SetShowDuration(text, 60000);
        arc.Tag = new QuotaTooltipTargets(trackRing, centerGlass, ringSize, ringStroke);
        ringHost.Children.Add(text);
        ring.Children.Add(ringHost);
        return ring;
    }

    private void UpdateIdleQuotaPanel(TaskCard card)
    {
        UpdateExternalBalancePanel(card);
        if (_rateLimits is null)
        {
            card.FiveHourQuotaRemainingPercent = null;
            card.ForcePixelRefresh = true;
            UpdateQuotaRing(card.WeeklyQuotaArc, card.WeeklyQuotaText, null, "--/--");
            UpdateQuotaRing(card.FiveHourQuotaArc, card.FiveHourQuotaText, null, "--:--");
            return;
        }

        if (card.FiveHourQuotaRemainingPercent is null ||
            Math.Abs(card.FiveHourQuotaRemainingPercent.Value - _rateLimits.FiveHourRemainingPercent) > 0.1)
        {
            card.FiveHourQuotaRemainingPercent = _rateLimits.FiveHourRemainingPercent;
            card.ForcePixelRefresh = true;
        }

        UpdateQuotaRing(
            card.WeeklyQuotaArc,
            card.WeeklyQuotaText,
            _rateLimits.WeeklyRemainingPercent,
            FormatQuotaResetDate(_rateLimits.WeeklyResetsAt));
        UpdateQuotaRing(
            card.FiveHourQuotaArc,
            card.FiveHourQuotaText,
            _rateLimits.FiveHourRemainingPercent,
            FormatQuotaResetTime(_rateLimits.FiveHourResetsAt));
    }

    private void UpdateExternalBalancePanel(TaskCard card)
    {
        SetExternalBalanceText(card.ShengshengBalanceText, _shengshengBalanceText, _shengshengBalanceUpdatedAtText);
        UpdateExternalBalanceDelta(card.ShengshengBalanceArrowText, card.ShengshengBalanceDeltaText, _shengshengConsumedAmount);
        SetExternalBalanceText(card.DeepkeyBalanceText, _deepkeyBalanceText, "");
        UpdateExternalBalanceDelta(card.DeepkeyBalanceArrowText, card.DeepkeyBalanceDeltaText, _deepkeyConsumedAmount);
    }

    private static void SetExternalBalanceText(TextBlock textBlock, string balanceText, string updatedAtText)
    {
        textBlock.Inlines.Clear();
        textBlock.Inlines.Add(new Run(balanceText));
        if (!string.IsNullOrWhiteSpace(updatedAtText))
        {
            textBlock.Inlines.Add(new Run(" " + updatedAtText)
            {
                Foreground = CreateFrozenBrush(Color.FromArgb(150, 210, 215, 226)),
                FontFamily = FontForChinese(),
                FontWeight = FontWeights.SemiBold,
                FontSize = 9.8,
            });
        }
    }

    private static void UpdateExternalBalanceDelta(TextBlock arrowText, TextBlock deltaText, double? consumed)
    {
        var consumedText = FormatExternalBalanceConsumption(consumed);
        arrowText.Text = string.IsNullOrWhiteSpace(consumedText) ? "" : "▼";
        deltaText.Text = consumedText;
    }

    private static string FormatExternalBalanceConsumption(double? consumed)
    {
        if (!consumed.HasValue)
        {
            return "";
        }

        var normalized = Math.Max(0, consumed.Value);
        if (normalized < 0.005)
        {
            normalized = 0;
        }

        return normalized.ToString(normalized >= 100 ? "#,0.##" : "0.##", CultureInfo.InvariantCulture);
    }

    private void SetExternalBalanceBaseline()
    {
        _shengshengBaselineAmount = _shengshengBalanceAmount;
        _deepkeyBaselineAmount = _deepkeyBalanceAmount;
        _shengshengLastObservedAmount = _shengshengBalanceAmount;
        _deepkeyLastObservedAmount = _deepkeyBalanceAmount;
        _shengshengConsumedAmount = _shengshengBalanceAmount.HasValue ? 0 : null;
        _deepkeyConsumedAmount = _deepkeyBalanceAmount.HasValue ? 0 : null;
        SaveExternalBalanceAccounting();
        UpdateExternalBalancePanels();
    }

    private void SaveExternalBalanceAccounting()
    {
        try
        {
            if (!File.Exists(_externalBalancesPath))
            {
                return;
            }

            var root = JsonNode.Parse(File.ReadAllText(_externalBalancesPath, Encoding.UTF8)) as JsonObject;
            if (root?["providers"] is not JsonArray providers)
            {
                return;
            }

            SetExternalBalanceAccountingNode(providers, "shengsheng", _shengshengBaselineAmount, _shengshengLastObservedAmount, _shengshengConsumedAmount);
            SetExternalBalanceAccountingNode(providers, "deepkey", _deepkeyBaselineAmount, _deepkeyLastObservedAmount, _deepkeyConsumedAmount);
            File.WriteAllText(
                _externalBalancesPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
        }
        catch (Exception ex)
        {
            DebugLog("external_balance_accounting_save_failed", new { error = ex.Message });
        }
    }

    private static void SetExternalBalanceAccountingNode(JsonArray providers, string providerId, double? baselineAmount, double? lastObservedAmount, double? consumedAmount)
    {
        foreach (var item in providers)
        {
            if (item is not JsonObject provider ||
                !string.Equals((string?)provider["id"], providerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            provider["baselineAmount"] = baselineAmount.HasValue ? JsonValue.Create(baselineAmount.Value) : null;
            provider["lastObservedAmount"] = lastObservedAmount.HasValue ? JsonValue.Create(lastObservedAmount.Value) : null;
            provider["consumedAmount"] = consumedAmount.HasValue ? JsonValue.Create(consumedAmount.Value) : null;
            return;
        }
    }

    private void UpdateExternalBalancePanels()
    {
        foreach (var card in _cards.Values.Distinct())
        {
            UpdateExternalBalancePanel(card);
        }
    }

    private async Task RefreshExternalBalancesAsync()
    {
        if (_externalBalanceRefreshInFlight)
        {
            return;
        }

        _externalBalanceRefreshInFlight = true;
        try
        {
            var providers = LoadExternalBalanceProviders();
            var accountingChanged = false;
            if (_showExternalBalances && providers.TryGetValue("shengsheng", out var shengsheng))
            {
                _shengshengBaselineAmount = shengsheng.BaselineAmount;
                _shengshengLastObservedAmount = shengsheng.LastObservedAmount;
                _shengshengConsumedAmount = shengsheng.ConsumedAmount;
                var snapshot = await FetchExternalBalanceSnapshotAsync(shengsheng);
                if (snapshot.HasValue)
                {
                    _shengshengBalanceText = snapshot.Value.DisplayText;
                    _shengshengBalanceAmount = snapshot.Value.Amount;
                    _shengshengBalancePrefix = snapshot.Value.CurrencyPrefix;
                    _shengshengBalanceUpdatedAtText = snapshot.Value.SecondaryText ?? "";
                    accountingChanged |= ApplyExternalBalanceAccounting("shengsheng", snapshot.Value.Amount, shengsheng);
                }
            }

            if (_showExternalBalances && _externalBalanceProviderCount == 2 && providers.TryGetValue("deepkey", out var deepkey))
            {
                _deepkeyBaselineAmount = deepkey.BaselineAmount;
                _deepkeyLastObservedAmount = deepkey.LastObservedAmount;
                _deepkeyConsumedAmount = deepkey.ConsumedAmount;
                var snapshot = await FetchExternalBalanceSnapshotAsync(deepkey);
                if (snapshot.HasValue)
                {
                    _deepkeyBalanceText = snapshot.Value.DisplayText;
                    _deepkeyBalanceAmount = snapshot.Value.Amount;
                    _deepkeyBalancePrefix = snapshot.Value.CurrencyPrefix;
                    accountingChanged |= ApplyExternalBalanceAccounting("deepkey", snapshot.Value.Amount, deepkey);
                }
            }

            if (accountingChanged)
            {
                SaveExternalBalanceAccounting();
            }

            UpdateExternalBalancePanels();
        }
        catch (Exception ex)
        {
            DebugLog("external_balance_refresh_failed", new { error = ex.Message });
        }
        finally
        {
            _externalBalanceRefreshInFlight = false;
        }
    }

    private bool ApplyExternalBalanceAccounting(string providerId, double? currentAmount, ExternalBalanceProviderConfig provider)
    {
        if (!currentAmount.HasValue)
        {
            return false;
        }

        var previousConsumed = provider.ConsumedAmount;
        var consumed = previousConsumed;
        if (!consumed.HasValue && provider.BaselineAmount.HasValue)
        {
            consumed = Math.Max(0, provider.BaselineAmount.Value - currentAmount.Value);
        }

        if (provider.LastObservedAmount.HasValue)
        {
            var spentSinceLastRefresh = provider.LastObservedAmount.Value - currentAmount.Value;
            if (spentSinceLastRefresh > 0.004)
            {
                consumed = Math.Max(0, consumed ?? 0) + spentSinceLastRefresh;
            }
        }

        if (providerId.Equals("shengsheng", StringComparison.OrdinalIgnoreCase))
        {
            _shengshengLastObservedAmount = currentAmount;
            _shengshengConsumedAmount = consumed;
        }
        else if (providerId.Equals("deepkey", StringComparison.OrdinalIgnoreCase))
        {
            _deepkeyLastObservedAmount = currentAmount;
            _deepkeyConsumedAmount = consumed;
        }

        return !NullableDoubleEquals(provider.LastObservedAmount, currentAmount) ||
               !NullableDoubleEquals(previousConsumed, consumed);
    }

    private static bool NullableDoubleEquals(double? left, double? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return left.HasValue == right.HasValue;
        }

        return Math.Abs(left.Value - right.Value) < 0.000001;
    }

    private Dictionary<string, ExternalBalanceProviderConfig> LoadExternalBalanceProviders()
    {
        var providers = new Dictionary<string, ExternalBalanceProviderConfig>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_externalBalancesPath))
        {
            return providers;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_externalBalancesPath, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("providers", out var providersElement) ||
                providersElement.ValueKind != JsonValueKind.Array)
            {
                return providers;
            }

            foreach (var provider in providersElement.EnumerateArray())
            {
                var id = ReadString(provider, "id");
                var baseUrl = ReadString(provider, "baseUrl");
                var kind = ReadString(provider, "kind");
                var systemToken = ReadString(provider, "systemToken") ?? ReadString(provider, "apiKey");
                var userId = ReadString(provider, "userId");
                var baselineAmount = ReadNullableDoubleProperty(provider, "baselineAmount");
                var lastObservedAmount = ReadNullableDoubleProperty(provider, "lastObservedAmount");
                var consumedAmount = ReadNullableDoubleProperty(provider, "consumedAmount");
                if (string.IsNullOrWhiteSpace(id) ||
                    string.IsNullOrWhiteSpace(baseUrl) ||
                    string.IsNullOrWhiteSpace(systemToken))
                {
                    continue;
                }

                var endpoints = new List<string>();
                if (provider.TryGetProperty("endpoints", out var endpointsElement) &&
                    endpointsElement.ValueKind == JsonValueKind.Array)
                {
                    endpoints.AddRange(endpointsElement.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))!);
                }

                if (endpoints.Count == 0)
                {
                    endpoints.AddRange(new[] { "/api/user/self", "/api/user/amount", "/api/user/quota_stats" });
                }

                providers[id] = new ExternalBalanceProviderConfig(id, baseUrl.TrimEnd('/'), kind, systemToken, userId, endpoints, baselineAmount, lastObservedAmount, consumedAmount);
            }
        }
        catch (Exception ex)
        {
            DebugLog("external_balance_config_failed", new { error = ex.Message });
        }

        return providers;
    }

    private ExternalBalanceEditorValues ReadExternalBalanceEditorValues()
    {
        try
        {
            if (!File.Exists(_externalBalancesPath))
            {
                return new ExternalBalanceEditorValues("省省", "", "", "Deepkey", "", "");
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(_externalBalancesPath, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array)
            {
                return new ExternalBalanceEditorValues("省省", "", "", "Deepkey", "", "");
            }

            string ReadValue(string providerId, string property, string fallback = "")
            {
                foreach (var provider in providers.EnumerateArray())
                {
                    if (string.Equals(ReadString(provider, "id"), providerId, StringComparison.OrdinalIgnoreCase))
                    {
                        var value = ReadString(provider, property);
                        return string.IsNullOrWhiteSpace(value) ? fallback : value;
                    }
                }

                return fallback;
            }

            var shengshengToken = ReadValue("shengsheng", "systemToken");
            if (string.IsNullOrWhiteSpace(shengshengToken))
            {
                shengshengToken = ReadValue("shengsheng", "apiKey");
            }

            var deepkeyToken = ReadValue("deepkey", "systemToken");
            if (string.IsNullOrWhiteSpace(deepkeyToken))
            {
                deepkeyToken = ReadValue("deepkey", "apiKey");
            }

            return new ExternalBalanceEditorValues(
                ReadValue("shengsheng", "displayName", "省省"),
                ReadValue("shengsheng", "userId"),
                shengshengToken,
                ReadValue("deepkey", "displayName", "Deepkey"),
                ReadValue("deepkey", "userId"),
                deepkeyToken);
        }
        catch (Exception ex)
        {
            DebugLog("external_balance_editor_load_failed", new { error = ex.Message });
            return new ExternalBalanceEditorValues("省省", "", "", "Deepkey", "", "");
        }
    }

    private void SaveExternalBalanceLayoutConfiguration(
        bool showBalances,
        int providerCount,
        string shengshengDisplayName,
        string shengshengUserId,
        string shengshengToken,
        string deepkeyDisplayName,
        string deepkeyUserId,
        string deepkeyToken)
    {
        _showExternalBalances = showBalances;
        _externalBalanceProviderCount = Math.Clamp(providerCount, 1, 2);

        if (showBalances)
        {
            SaveExternalBalanceProviderCredentials(
                shengshengUserId,
                shengshengToken,
                shengshengDisplayName,
                deepkeyUserId,
                deepkeyToken,
                deepkeyDisplayName,
                _externalBalanceProviderCount);
        }

        SaveUiSettings();
        DebugLog("external_balance_layout_saved", new
        {
            visible = _showExternalBalances,
            providerCount = _externalBalanceProviderCount,
        });
        HardRefreshStatusLight();
        _ = RefreshExternalBalancesAsync();
    }

    private void SaveExternalBalanceProviderCredentials(
        string shengshengUserId,
        string shengshengToken,
        string shengshengDisplayName,
        string deepkeyUserId,
        string deepkeyToken,
        string deepkeyDisplayName,
        int providerCount)
    {
        try
        {
            JsonObject root;
            if (File.Exists(_externalBalancesPath))
            {
                root = JsonNode.Parse(File.ReadAllText(_externalBalancesPath, Encoding.UTF8)) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var providers = root["providers"] as JsonArray ?? new JsonArray();
            root["providers"] = providers;

            var shengsheng = EnsureExternalBalanceProvider(
                providers,
                "shengsheng",
                "https://yuanyuaicloud.cn",
                "yuanyuan-window",
                "/api/query-quota");
            shengsheng["userId"] = shengshengUserId.Trim();
            shengsheng["systemToken"] = shengshengToken.Trim();
            shengsheng["displayName"] = NormalizeExternalBalanceDisplayName(shengshengDisplayName, "省省");

            if (providerCount == 2)
            {
                var deepkey = EnsureExternalBalanceProvider(
                    providers,
                    "deepkey",
                    "https://deepkey.top",
                    "",
                    "/api/status",
                    "/api/user/self");
                deepkey["userId"] = deepkeyUserId.Trim();
                deepkey["systemToken"] = deepkeyToken.Trim();
                deepkey["displayName"] = NormalizeExternalBalanceDisplayName(deepkeyDisplayName, "Deepkey");
            }

            File.WriteAllText(
                _externalBalancesPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            DebugLog("external_balance_editor_save_failed", new { error = ex.Message });
        }
    }

    private static JsonObject EnsureExternalBalanceProvider(
        JsonArray providers,
        string id,
        string baseUrl,
        string kind,
        params string[] endpoints)
    {
        foreach (var item in providers)
        {
            if (item is JsonObject provider && string.Equals((string?)provider["id"], id, StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        var endpointList = new JsonArray();
        foreach (var endpoint in endpoints)
        {
            endpointList.Add(endpoint);
        }

        var created = new JsonObject
        {
            ["id"] = id,
            ["baseUrl"] = baseUrl,
            ["kind"] = kind,
            ["displayName"] = id.Equals("shengsheng", StringComparison.OrdinalIgnoreCase) ? "省省" : "Deepkey",
            ["userId"] = "",
            ["systemToken"] = "",
            ["endpoints"] = endpointList,
        };
        providers.Add(created);
        return created;
    }

    private static string NormalizeExternalBalanceDisplayName(string? value, string fallback)
    {
        var normalized = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized[..Math.Min(normalized.Length, 24)];
    }

    private async Task<ExternalBalanceSnapshot?> FetchExternalBalanceSnapshotAsync(ExternalBalanceProviderConfig provider)
    {
        if (string.Equals(provider.Kind, "yuanyuan-window", StringComparison.OrdinalIgnoreCase) ||
            provider.BaseUrl.Contains("yuanyuaicloud.cn", StringComparison.OrdinalIgnoreCase))
        {
            return await FetchYuanyuanWindowQuotaSnapshotAsync(provider);
        }

        if (!string.IsNullOrWhiteSpace(provider.UserId))
        {
            return await FetchNewApiBalanceSnapshotAsync(provider);
        }

        foreach (var endpoint in provider.Endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, provider.BaseUrl + endpoint);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + provider.SystemToken);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
                if (!string.IsNullOrWhiteSpace(provider.UserId))
                {
                    request.Headers.TryAddWithoutValidation("New-Api-User", provider.UserId);
                }
                using var response = await ExternalBalanceHttpClient.SendAsync(request);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    DebugLog("external_balance_unauthorized", new { provider = provider.Id, endpoint });
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(content);
                if (TryExtractBalanceText(doc.RootElement, out var balanceText))
                {
                    return new ExternalBalanceSnapshot(balanceText, null, ExtractCurrencyPrefix(balanceText), null);
                }
            }
            catch (Exception ex)
            {
                DebugLog("external_balance_endpoint_failed", new { provider = provider.Id, endpoint, error = ex.Message });
            }
        }

        return null;
    }

    private async Task<ExternalBalanceSnapshot?> FetchYuanyuanWindowQuotaSnapshotAsync(ExternalBalanceProviderConfig provider)
    {
        try
        {
            var token = provider.SystemToken.Trim();
            if (token.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
            {
                token = token[3..];
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, provider.BaseUrl + "/api/query-quota");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
            using var response = await ExternalBalanceHttpClient.SendAsync(request);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                DebugLog("external_balance_unauthorized", new { provider = provider.Id, endpoint = "/api/query-quota" });
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                DebugLog("external_balance_http_failed", new { provider = provider.Id, endpoint = "/api/query-quota", status = (int)response.StatusCode });
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var successProp) &&
                successProp.ValueKind == JsonValueKind.False)
            {
                var message = ReadString(root, "message");
                return string.IsNullOrWhiteSpace(message)
                    ? null
                    : new ExternalBalanceSnapshot(message, null, "", null);
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var windowRemain = ReadDoubleProperty(data, "windowRemain", fallback: double.NaN);
            if (double.IsNaN(windowRemain))
            {
                return null;
            }

            var resetText = "";
            var nextResetTime = ReadDoubleProperty(data, "nextResetTime", fallback: 0);
            if (nextResetTime > 0)
            {
                resetText = DateTimeOffset.FromUnixTimeSeconds((long)nextResetTime)
                    .ToLocalTime()
                    .ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            return new ExternalBalanceSnapshot(
                "₸" + windowRemain.ToString(windowRemain >= 100 ? "#,0" : "0.##", CultureInfo.InvariantCulture),
                windowRemain,
                "₸",
                resetText);
        }
        catch (TaskCanceledException)
        {
            DebugLog("external_balance_timeout", new { provider = provider.Id });
            return null;
        }
        catch (Exception ex)
        {
            DebugLog("external_balance_endpoint_failed", new { provider = provider.Id, endpoint = "/api/query-quota", error = ex.Message });
            return null;
        }
    }

    private async Task<ExternalBalanceSnapshot?> FetchNewApiBalanceSnapshotAsync(ExternalBalanceProviderConfig provider)
    {
        if (string.IsNullOrWhiteSpace(provider.UserId))
        {
            DebugLog("external_balance_config_failed", new { provider = provider.Id, error = "missing_user_id" });
            return null;
        }

        try
        {
            var status = await FetchShengshengStatusAsync(provider.BaseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, provider.BaseUrl + "/api/user/self");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + provider.SystemToken);
            request.Headers.TryAddWithoutValidation("New-Api-User", provider.UserId);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
            using var response = await ExternalBalanceHttpClient.SendAsync(request);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                DebugLog("external_balance_unauthorized", new { provider = provider.Id, endpoint = "/api/user/self" });
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                DebugLog("external_balance_http_failed", new { provider = provider.Id, endpoint = "/api/user/self", status = (int)response.StatusCode });
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var successProp) &&
                successProp.ValueKind == JsonValueKind.False)
            {
                var message = ReadString(root, "message");
                return string.IsNullOrWhiteSpace(message)
                    ? null
                    : new ExternalBalanceSnapshot(message, null, "", null);
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var quota = ReadDoubleProperty(data, "quota");
            var commissionQuota = ReadDoubleProperty(data, "commission_quota");
            var affQuota = ReadDoubleProperty(data, "aff_quota");
            var remainQuota = quota + commissionQuota + affQuota;
            return FormatExternalBalanceQuota(remainQuota, status);
        }
        catch (TaskCanceledException)
        {
            DebugLog("external_balance_timeout", new { provider = provider.Id });
            return null;
        }
        catch (Exception ex)
        {
            DebugLog("external_balance_endpoint_failed", new { provider = provider.Id, endpoint = "/api/user/self", error = ex.Message });
            return null;
        }
    }

    private static async Task<ShengshengStatusConfig> FetchShengshengStatusAsync(string baseUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/status");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
        using var response = await ExternalBalanceHttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var data = doc.RootElement.TryGetProperty("data", out var dataElement)
            ? dataElement
            : doc.RootElement;

        return new ShengshengStatusConfig(
            ReadDoubleProperty(data, "quota_per_unit", fallback: 500000),
            ReadString(data, "quota_display_type") ?? "TOKENS",
            ReadDoubleProperty(data, "usd_exchange_rate", fallback: 7),
            ReadString(data, "custom_currency_symbol") ?? "¤",
            ReadDoubleProperty(data, "custom_currency_exchange_rate", fallback: 1));
    }

    private static ExternalBalanceSnapshot FormatExternalBalanceQuota(double quota, ShengshengStatusConfig status)
    {
        var quotaPerUnit = Math.Abs(status.QuotaPerUnit) < 0.000001 ? 1 : status.QuotaPerUnit;
        var displayType = status.QuotaDisplayType.ToUpperInvariant();
        if (displayType == "TOKENS")
        {
            return new ExternalBalanceSnapshot(quota.ToString("#,0", CultureInfo.InvariantCulture), quota, "", null);
        }

        var prefix = displayType switch
        {
            "USD" => "$",
            "CNY" => "¥",
            "CUSTOM" => status.CustomCurrencySymbol,
            _ => ""
        };
        var multiplier = displayType switch
        {
            "CNY" => status.UsdExchangeRate,
            "CUSTOM" => status.CustomCurrencyExchangeRate,
            _ => 1.0
        };
        var amount = quota / quotaPerUnit * multiplier;
        return new ExternalBalanceSnapshot(prefix + amount.ToString(amount >= 100 ? "#,0.##" : "0.##", CultureInfo.InvariantCulture), amount, prefix, null);
    }

    private static double ReadDoubleProperty(JsonElement element, string name, double fallback = 0)
    {
        return element.TryGetProperty(name, out var value) && TryReadDouble(value, out var result)
            ? result
            : fallback;
    }

    private static double? ReadNullableDoubleProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && TryReadDouble(value, out var result)
            ? result
            : null;
    }

    private static string ExtractCurrencyPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var first = text.TrimStart()[0];
        return char.IsDigit(first) || first == '-' || first == '+'
            ? ""
            : first.ToString();
    }

    private static bool TryExtractBalanceText(JsonElement element, out string balanceText)
    {
        balanceText = "";
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "remain_quota", "remaining_quota", "balance", "amount", "credit", "quota", "money" })
            {
                if (element.TryGetProperty(name, out var value) && TryFormatBalanceValue(value, out balanceText))
                {
                    return true;
                }
            }

            if (element.TryGetProperty("data", out var data) && TryExtractBalanceText(data, out balanceText))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryExtractBalanceText(property.Value, out balanceText))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryExtractBalanceText(item, out balanceText))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFormatBalanceValue(JsonElement value, out string balanceText)
    {
        balanceText = "";
        if (TryReadDouble(value, out var numeric))
        {
            balanceText = numeric switch
            {
                >= 1000 => numeric.ToString("#,0", CultureInfo.InvariantCulture),
                >= 10 => numeric.ToString("0.#", CultureInfo.InvariantCulture),
                _ => numeric.ToString("0.##", CultureInfo.InvariantCulture),
            };
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                balanceText = text.Trim();
                return true;
            }
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private void ToggleQuotaPinned()
    {
        _isQuotaPinned = !_isQuotaPinned;
        SaveUiSettings();
        foreach (var card in _cards.Values.Distinct())
        {
            UpdateQuotaPinVisual(card);
        }

        DebugLog("quota_pin_toggle", new { pinned = _isQuotaPinned });
        Render();
    }

    private void PinQuotaPageFromMenu()
    {
        if (_isQuotaPinned)
        {
            _isQuotaPinned = false;
            SaveUiSettings();
            foreach (var card in _cards.Values.Distinct())
            {
                UpdateQuotaPinVisual(card);
            }

            DebugLog("quota_unpin_menu", new { pinned = false, collapsed = _isCollapsed });
            Render();
            return;
        }

        if (_cards.TryGetValue("idle", out var existingQuotaCard))
        {
            RemoveCardImmediately("idle", existingQuotaCard);
        }

        _tasks.Remove("idle");
        _isQuotaPinned = true;
        SaveUiSettings();
        DebugLog("quota_pin_menu", new { pinned = true, collapsed = _isCollapsed });
        Render();
    }

    private void ToggleDingDong()
    {
        _dingDongEnabled = !_dingDongEnabled;
        SaveUiSettings();
        DebugLog("dingdong_toggle", new { enabled = _dingDongEnabled });
    }

    private void ToggleQuotaDisplayMode()
    {
        _showQuotaPercentInRing = !_showQuotaPercentInRing;
        SaveUiSettings();
        foreach (var card in _cards.Values.Distinct())
        {
            if (card.QuotaPanel.Visibility == Visibility.Visible)
            {
                UpdateIdleQuotaPanel(card);
            }
        }

        DebugLog("quota_display_mode_toggle", new { percentInRing = _showQuotaPercentInRing });
        Render();
    }

    private void UpdateQuotaPinVisual(TaskCard card)
    {
        var color = _isQuotaPinned
            ? Color.FromArgb(246, 255, 255, 255)
            : Color.FromArgb(168, 230, 236, 246);
        var stroke = CreateFrozenBrush(color);
        card.QuotaPinHead.Stroke = Brushes.Transparent;
        card.QuotaPinHead.Fill = stroke;
        card.QuotaPinNeedle.Stroke = stroke;
        card.QuotaPinButton.Opacity = _isQuotaPinned ? 1.0 : 0.78;
        card.QuotaPinButton.ToolTip = CreateControlToolTip(_isQuotaPinned ? Ui("松开", "Unpin") : Ui("固定", "Pin"));
    }

    private void UpdateQuotaRing(ShapePath arc, TextBlock text, double? remainingPercent, string resetText)
    {
        if (remainingPercent is null)
        {
            arc.Data = Geometry.Empty;
            text.Text = _showQuotaPercentInRing ? "--%" : resetText;
            SetQuotaRingTooltip(arc, text, _showQuotaPercentInRing ? resetText : "--%");
            return;
        }

        var percent = Math.Clamp(remainingPercent.Value, 0, 100);
        var percentText = $"{percent:0.#}%";
        var ringSize = QuotaRingSize;
        var ringStroke = QuotaRingStroke;
        if (arc.Tag is QuotaTooltipTargets targets)
        {
            ringSize = targets.RingSize;
            ringStroke = targets.RingStroke;
        }
        arc.Data = CreateProgressArcGeometry(percent, ringSize, ringStroke);
        text.Text = _showQuotaPercentInRing ? percentText : resetText;
        SetQuotaRingTooltip(arc, text, _showQuotaPercentInRing ? resetText : percentText);
    }

    private void SetQuotaRingTooltip(ShapePath arc, TextBlock text, string tooltip)
    {
        ApplyQuotaTooltip(arc, tooltip);
        ApplyQuotaTooltip(text, tooltip);
        if (arc.Tag is QuotaTooltipTargets targets)
        {
            ApplyQuotaTooltip(targets.Track, tooltip);
            ApplyQuotaTooltip(targets.Center, tooltip);
        }
    }

    private void ApplyQuotaTooltip(FrameworkElement target, string tooltip)
    {
        target.ToolTip = CreateQuotaToolTip(tooltip);
    }

    private ToolTip CreateQuotaToolTip(string text)
    {
        var scale = UiScale;
        return CreateRoundedToolTip(
            text,
            FontForDuration(),
            FontWeights.Black,
            26 * scale,
            ScaleThickness(15, 9, 15, 10),
            10 * scale,
            1.2 * scale,
            18 * scale,
            0.48,
            Color.FromArgb(238, 18, 22, 30),
            Color.FromArgb(130, 232, 238, 250),
            Color.FromRgb(236, 242, 252));
    }

    private ToolTip CreateControlToolTip(string text)
    {
        var scale = UiScale;
        return CreateRoundedToolTip(
            text,
            FontForChinese(),
            FontWeights.SemiBold,
            14 * scale,
            ScaleThickness(10, 5.5, 10, 6.5),
            8 * scale,
            1 * scale,
            12 * scale,
            0.42,
            Color.FromArgb(236, 18, 22, 30),
            Color.FromArgb(112, 232, 238, 250),
            Color.FromRgb(235, 240, 250));
    }

    private static ToolTip CreateRoundedToolTip(
        string text,
        FontFamily fontFamily,
        FontWeight fontWeight,
        double fontSize,
        Thickness padding,
        double cornerRadius,
        double borderThickness,
        double blurRadius,
        double shadowOpacity,
        Color background,
        Color border,
        Color foreground)
    {
        TextBlock CreateToolTipText(Visibility visibility)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = fontFamily,
                FontWeight = fontWeight,
                FontSize = fontSize,
                Foreground = CreateFrozenBrush(foreground),
                TextAlignment = TextAlignment.Center,
                Visibility = visibility,
            };
        }

        var content = new Grid
        {
            Children =
            {
                new Border
                {
                    CornerRadius = new CornerRadius(cornerRadius),
                    Padding = padding,
                    Background = CreateFrozenBrush(background),
                    BorderBrush = CreateFrozenBrush(border),
                    BorderThickness = new Thickness(borderThickness),
                    SnapsToDevicePixels = true,
                    IsHitTestVisible = false,
                    Child = CreateToolTipText(Visibility.Hidden),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(0, 0, 0),
                        BlurRadius = blurRadius,
                        ShadowDepth = 0,
                        Opacity = shadowOpacity,
                    },
                },
                new Border
                {
                    CornerRadius = new CornerRadius(cornerRadius),
                    Padding = padding,
                    Background = CreateFrozenBrush(background),
                    BorderBrush = CreateFrozenBrush(border),
                    BorderThickness = new Thickness(borderThickness),
                    SnapsToDevicePixels = true,
                    Child = CreateToolTipText(Visibility.Visible),
                },
            },
        };
        TextOptions.SetTextFormattingMode(content, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(content, TextRenderingMode.Auto);
        TextOptions.SetTextHintingMode(content, TextHintingMode.Fixed);

        return new ToolTip
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HasDropShadow = false,
            Content = content,
        };
    }

    private static string GetDefaultStateDir()
    {
        return IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Codexstar");
    }

    private void MigrateLegacySettingsIfNeeded(bool usingDefaultStateDir)
    {
        if (!usingDefaultStateDir || File.Exists(_settingsPath))
        {
            return;
        }

        var legacySettingsPath = @"E:\Tempscript\CodexStatusLight\settings.json";
        try
        {
            if (File.Exists(legacySettingsPath))
            {
                File.Copy(legacySettingsPath, _settingsPath, overwrite: false);
            }
        }
        catch
        {
        }
    }

    private static string FormatQuotaResetDate(DateTime? resetsAtUtc)
    {
        return resetsAtUtc is null
            ? "--/--"
            : resetsAtUtc.Value.ToLocalTime().ToString("MM/dd", CultureInfo.InvariantCulture);
    }

    private static string FormatQuotaResetTime(DateTime? resetsAtUtc)
    {
        return resetsAtUtc is null
            ? "--:--"
            : resetsAtUtc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static Brush CreateQuotaCenterGlassBrush(Color accent)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.86,
            RadiusY = 0.86,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(248, 20, 25, 33), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(250, 14, 18, 25), 0.62));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(253, 5, 8, 13), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Geometry CreateProgressArcGeometry(double percent, double size, double strokeThickness)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent <= 0.05)
        {
            return Geometry.Empty;
        }

        var radius = (size - strokeThickness) / 2.0;
        var center = new Point(size / 2.0, size / 2.0);
        if (percent >= 99.95)
        {
            return new EllipseGeometry(center, radius, radius);
        }

        var usedPercent = 100.0 - percent;
        var startAngle = -90.0 + usedPercent / 100.0 * 359.8;
        var endAngle = -90.0 + 359.8;
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, endAngle);
        return new PathGeometry(new[]
        {
            new PathFigure(
                start,
                new PathSegment[]
                {
                    new ArcSegment(
                        end,
                        new Size(radius, radius),
                        0,
                        percent > 50,
                        SweepDirection.Clockwise,
                        true)
                },
                false)
        });
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private void UpdateCard(TaskCard card, TaskState task, int index, int total, bool suppressStatusFlash = false)
    {
        var palette = GetPalette(task.Status);
        var previousStatus = card.Status;
        card.Root.Tag = task.TurnId;
        card.Root.Cursor = task.Status == TaskVisualStatus.Error ? Cursors.Hand : Cursors.Arrow;
        var isIdle = task.Status == TaskVisualStatus.Idle;
        card.ContentGrid.Visibility = isIdle ? Visibility.Collapsed : Visibility.Visible;
        card.QuotaPanel.Visibility = isIdle ? Visibility.Visible : Visibility.Collapsed;
        card.QuotaPinButton.Visibility = isIdle ? Visibility.Visible : Visibility.Collapsed;
        card.QuotaResetButton.Visibility = isIdle ? Visibility.Visible : Visibility.Collapsed;
        UpdateQuotaPinVisual(card);
        if (isIdle)
        {
            UpdateIdleQuotaPanel(card);
        }

        card.Title.Text = GetDisplayTitle(task);
        card.Message.Text = task.Status == TaskVisualStatus.Error ? GetDisplayMessage(task.Message) : "";
        card.Message.Visibility = task.Status == TaskVisualStatus.Error ? Visibility.Visible : Visibility.Collapsed;
        card.BadgeText.Text = GetStatusLabel(task);
        card.Duration.Text = FormatDuration(task);
        card.ToggleButton.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        card.ToggleButton.ToolTip = CreateControlToolTip(Ui("折叠", "Collapse"));
        var visualChanged = !card.PaletteInitialized || previousStatus != task.Status;
        if (visualChanged)
        {
            card.Surface.Background = CreateFrozenBrush(palette.Surface);
            card.Surface.BorderBrush = CreatePanelRimBrush(palette);

            if (card.Glass is not null)
            {
                card.Glass.Background = CreateGlassBrush(palette);
            }

            BuildEdgeHighlights(card.EdgeHighlights, palette);

            UpdateBadgeVisual(card.Badge, palette);
            card.BadgeText.Foreground = CreateBadgeTextBrush(palette);
            card.DurationAura.Background = CreateDurationTrailAuraBrush(palette);
            card.DurationCore.Background = CreateDurationTrailCoreBrush(palette);
            if (card.DurationAura.Effect is System.Windows.Media.Effects.DropShadowEffect durationAuraGlow)
            {
                durationAuraGlow.Color = palette.Accent;
                durationAuraGlow.Opacity = task.Status == TaskVisualStatus.Idle ? 0.14 : 0.28;
            }

            if (card.DurationCore.Effect is System.Windows.Media.Effects.DropShadowEffect durationCoreGlow)
            {
                durationCoreGlow.Color = palette.Highlight;
                durationCoreGlow.Opacity = task.Status == TaskVisualStatus.Idle ? 0.14 : 0.24;
            }

            UpdatePixelBrushes(card, palette, task.Status);
            card.PaletteInitialized = true;
        }

        card.Status = task.Status;
        card.Palette = palette;
        if (!suppressStatusFlash && previousStatus != task.Status)
        {
            FlashStatusChange(card, task.Status);
        }
    }

    private void BuildPixels(TaskCard card, StatusPalette palette, TaskVisualStatus status)
    {
        card.Pixels.Children.Clear();
        card.PixelRects.Clear();
        card.PixelActiveStates.Clear();
        UpdatePixelBrushes(card, palette, status);
        const double size = 7.0;
        const double gap = 3.8;

        for (var row = 0; row < PixelRows; row++)
        {
            for (var col = 0; col < PixelCols; col++)
            {
                var offset = row % 2 == 0 ? 0 : 3.8;
                var rect = new Rectangle
                {
                    Width = size,
                    Height = size,
                    RadiusX = 1.35,
                    RadiusY = 1.35,
                    Fill = card.PixelInactiveBrush,
                    Opacity = 0.16,
                    SnapsToDevicePixels = true,
                };
                Canvas.SetLeft(rect, 6 + offset + col * (size + gap));
                Canvas.SetTop(rect, 7 + row * (size + gap));
                card.Pixels.Children.Add(rect);
                card.PixelRects.Add(rect);
                card.PixelActiveStates.Add(false);
            }
        }
    }

    private static void UpdatePixelBrushes(TaskCard card, StatusPalette palette, TaskVisualStatus status)
    {
        if (status == TaskVisualStatus.Idle)
        {
            card.PixelActiveBrush = CreateFrozenBrush(Color.FromRgb(166, 213, 244));
            card.PixelInactiveBrush = CreateFrozenBrush(Color.FromRgb(74, 91, 108));
        }
        else
        {
            card.PixelActiveBrush = CreateFrozenBrush(palette.Highlight);
            card.PixelInactiveBrush = CreateFrozenBrush(palette.Accent);
        }

        card.ForcePixelRefresh = true;
    }

    private static void UpdateBadgeVisual(Border badge, StatusPalette palette)
    {
        if (badge.Tag is BadgeLayers layers)
        {
            layers.Fill.Background = CreateSoftBadgeBrush(palette);
            layers.Rim.Fill = CreateBadgeRimBrush(palette);
            return;
        }

        badge.Background = CreateSoftBadgeBrush(palette);
        badge.BorderBrush = CreateBadgeRimBrush(palette);
    }

    private static void UpdateBadgeRimGeometry(ShapePath rim, double width, double height, double thickness, double radius)
    {
        if (width <= thickness * 2 || height <= thickness * 2)
        {
            rim.Data = Geometry.Empty;
            return;
        }

        var inset = 0.65;
        var outerRect = new Rect(
            inset,
            inset,
            Math.Max(0, width - inset * 2.0),
            Math.Max(0, height - inset * 2.0));
        var innerInset = inset + thickness;
        var innerRect = new Rect(
            innerInset,
            innerInset,
            Math.Max(0, width - innerInset * 2.0),
            Math.Max(0, height - innerInset * 2.0));
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(outerRect, Math.Max(0, radius - inset * 0.25), Math.Max(0, radius - inset * 0.25)));
        geometry.Children.Add(new RectangleGeometry(innerRect, Math.Max(0, radius - thickness), Math.Max(0, radius - thickness)));
        rim.Data = geometry;
    }

    private static void BuildEdgeHighlights(Grid layer, StatusPalette palette)
    {
        layer.Children.Clear();

        layer.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(2),
            BorderBrush = CreatePerimeterBrush(palette),
            Background = CreateContinuousMembraneBrush(palette),
            Opacity = 0.92,
            IsHitTestVisible = false,
        });

        layer.Children.Add(new Border
        {
            Margin = new Thickness(1.2),
            CornerRadius = new CornerRadius(18.8),
            BorderThickness = new Thickness(1),
            BorderBrush = CreateInnerPerimeterBrush(palette),
            Opacity = 0.74,
            IsHitTestVisible = false,
        });

        layer.Children.Add(new Border
        {
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(0.8),
            BorderBrush = CreateCornerWeightedRimBrush(palette),
            Opacity = 0.48,
            IsHitTestVisible = false,
        });
    }

    private static Brush CreateContinuousMembraneBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(34, 255, 255, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 12), 0.16));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(4, 255, 255, 255), 0.48));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 10), 0.72));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(44, 3, 5, 10), 1.00));
        return brush;
    }

    private static Brush CreatePerimeterBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(112, 255, 255, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 82), 0.18));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 34), 0.46));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(34, 255, 255, 255), 0.68));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 76), 1.00));
        return brush;
    }

    private static Brush CreateInnerPerimeterBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(1, 0),
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 34), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(14, 255, 255, 255), 0.36));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 28), 0.72));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(48, 255, 255, 255), 1.00));
        return brush;
    }

    private static Brush CreateCornerWeightedRimBrush(StatusPalette palette)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.08, 0.12),
            GradientOrigin = new Point(0.08, 0.12),
            RadiusX = 1.05,
            RadiusY = 1.15,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(84, 255, 255, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 52), 0.24));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 18), 0.62));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(34, 255, 255, 255), 1.00));
        return brush;
    }

    private void AnimatePixels()
    {
        if (_isCollapsed)
        {
            return;
        }

        _tick++;
        foreach (var card in _cards.Values.Distinct())
        {
            var forceRefresh = card.ForcePixelRefresh;
            for (var i = 0; i < card.PixelRects.Count; i++)
            {
                var col = i % PixelCols;
                var row = i / PixelCols;
                var wave = (_tick + col * 2 + row * 4) % 64;
                var sweep = Math.Abs(((_tick * 1.3 + row * 7) % (PixelCols + 18)) - col);
                var active = card.Status switch
                {
                    TaskVisualStatus.Idle => IsQuotaPixelActive(col, row, _tick, card.FiveHourQuotaRemainingPercent),
                    TaskVisualStatus.Working => wave < 18 || sweep < 5,
                    TaskVisualStatus.Done => sweep < 8 || (col + row + _tick / 5) % 9 == 0,
                    TaskVisualStatus.Input => wave is < 8 or > 55,
                    TaskVisualStatus.Error => IsErrorDropRippleActive(col, row, _tick),
                    _ => false,
                };
                if (forceRefresh || i >= card.PixelActiveStates.Count || card.PixelActiveStates[i] != active)
                {
                    var activeOpacity = card.Status switch
                    {
                        TaskVisualStatus.Idle => 0.46,
                        TaskVisualStatus.Error => 0.48,
                        _ => 0.40,
                    };
                    var inactiveOpacity = card.Status switch
                    {
                        TaskVisualStatus.Idle => 0.038,
                        TaskVisualStatus.Error => 0.055,
                        _ => 0.08,
                    };
                    card.PixelRects[i].Fill = active ? card.PixelActiveBrush : card.PixelInactiveBrush;
                    card.PixelRects[i].Opacity = active ? activeOpacity : inactiveOpacity;
                    if (i < card.PixelActiveStates.Count)
                    {
                        card.PixelActiveStates[i] = active;
                    }
                }
            }

            card.ForcePixelRefresh = false;
        }
    }

    private static bool IsQuotaPixelActive(int col, int row, int tick, double? remainingPercent)
    {
        if (remainingPercent is null)
        {
            return (tick / 18 + col * 3 + row * 5) % 37 == 0;
        }

        var percent = Math.Clamp(remainingPercent.Value, 0, 100);
        if (percent >= 99.5)
        {
            return true;
        }

        if (percent <= 0.5)
        {
            return false;
        }

        var totalPixels = PixelRows * PixelCols;
        var targetPixels = (int)Math.Round(totalPixels * percent / 100.0);
        var staggeredRow = (row * 19 + col * 5) % PixelRows;
        var fillRank = col * PixelRows + staggeredRow;
        if (fillRank < targetPixels)
        {
            return true;
        }

        var remainingCols = PixelCols * percent / 100.0;
        if (col <= remainingCols || remainingCols >= PixelCols - 1.0)
        {
            return false;
        }

        var rowSeed = (row * 29 + col * 11) % 19;
        var rightwardPhase = (tick * 0.24 + rowSeed) % (PixelCols + 7);
        var particleCol = remainingCols + rightwardPhase;
        var nearParticle = Math.Abs(col - particleCol) < 0.44;
        var sparseRow = (row + rowSeed + tick / 12) % 8 == 0;
        return nearParticle && sparseRow;
    }

    private static bool IsErrorDropRippleActive(int col, int row, int tick)
    {
        const double ringSpacing = 11.8;
        var originCol = PixelCols * 0.14;
        var originRow = (PixelRows - 1) / 2.0;
        var dx = col - originCol;
        var dy = (row - originRow) * 1.72;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var phase = (tick * 0.20) % ringSpacing;
        var ring = Math.Abs((distance - phase) % ringSpacing);
        if (ring > ringSpacing / 2)
        {
            ring = ringSpacing - ring;
        }

        var leftBias = Math.Max(0, 1.0 - col / 18.0);
        var ringWidth = 1.85 + leftBias * 0.85;
        var sourcePulse = distance < 2.2 && tick % 28 < 15;
        var dropHint = Math.Abs(col - originCol) <= 2.8 && Math.Abs(row - originRow) <= 1.0 && tick % 36 < 12;
        return sourcePulse || dropHint || ring < ringWidth;
    }

    private int GetSlideSide()
    {
        var area = SystemParameters.WorkArea;
        if (double.IsNaN(Left))
        {
            return 1;
        }

        var center = Left + Width / 2;
        return center < area.Left + area.Width / 2 ? -1 : 1;
    }

    private static bool IsValidCompletedAt(DateTime completedAt)
    {
        return completedAt > DateTime.MinValue.AddDays(1) && completedAt <= DateTime.UtcNow.AddMinutes(5);
    }

    private double UiScale => Math.Clamp(_uiScalePercent, MinUiScalePercent, MaxUiScalePercent) / 100.0;

    private ScaleTransform CreateUiScaleTransform()
    {
        var scale = UiScale;
        return new ScaleTransform(scale, scale);
    }

    private double ScaleValue(double value)
    {
        return value * UiScale;
    }

    private static double ScaleValue(double value, double scale)
    {
        return value * scale;
    }

    private Thickness ScaleThickness(double left, double top, double right, double bottom)
    {
        return ScaleThickness(left, top, right, bottom, UiScale);
    }

    private static Thickness ScaleThickness(double left, double top, double right, double bottom, double scale)
    {
        return new Thickness(left * scale, top * scale, right * scale, bottom * scale);
    }

    private CornerRadius ScaleCornerRadius(double radius)
    {
        return ScaleCornerRadius(radius, UiScale);
    }

    private static CornerRadius ScaleCornerRadius(double radius, double scale)
    {
        return new CornerRadius(radius * scale);
    }

    private double GetLogicalWindowWidth()
    {
        return double.IsNaN(Width) || Width <= 0
            ? CardWidth + PanelPadding * 2
            : Width / UiScale;
    }

    private void ApplyUiScaleTransform()
    {
        var scale = UiScale;
        Root.RenderTransform = Transform.Identity;
        if (Root.LayoutTransform is ScaleTransform transform)
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }
        else
        {
            Root.LayoutTransform = new ScaleTransform(scale, scale);
        }

        Root.RenderTransformOrigin = new Point(0, 0);
    }

    private void SetUiScalePercent(double percent)
    {
        var clamped = Math.Clamp(percent, MinUiScalePercent, MaxUiScalePercent);
        if (Math.Abs(_uiScalePercent - clamped) < 0.05)
        {
            return;
        }

        _uiScalePercent = clamped;
        ApplyUiScaleTransform();
        SaveUiSettings();
        _collapsedSignature = null;
        _expandedRenderSignature = null;
        _collapsedWidth = double.NaN;
        DebugLog("scale_set", new { scalePercent = Math.Round(_uiScalePercent, 1), collapsed = _isCollapsed });
        Render();
    }

    private void ResizeKeepingTop(double targetHeight, double targetWidth = CardWidth + PanelPadding * 2, double minWidth = 96)
    {
        targetHeight = Math.Max(CollapsedHeight, targetHeight);
        targetWidth = Math.Max(minWidth, targetWidth);
        var scale = UiScale;
        var physicalTargetHeight = targetHeight * scale;
        var physicalTargetWidth = targetWidth * scale;
        Root.Width = targetWidth;
        Root.Height = targetHeight;
        var area = SystemParameters.WorkArea;
        var currentWidth = double.IsNaN(Width) || Width <= 0 ? physicalTargetWidth : Width;
        var currentLeft = double.IsNaN(Left) ? area.Right - currentWidth - 24 : Left;
        var currentTop = double.IsNaN(Top) || Top <= 0 ? area.Bottom - physicalTargetHeight - 28 : Top;
        var preserveRight = currentLeft + currentWidth / 2 >= area.Left + area.Width / 2;
        var nextLeft = preserveRight ? currentLeft + currentWidth - physicalTargetWidth : currentLeft;
        Width = physicalTargetWidth;
        Height = physicalTargetHeight;
        Left = Math.Clamp(nextLeft, area.Left + 12, area.Right - Width - 12);
        Top = Math.Clamp(currentTop, area.Top + 12, area.Bottom - Height - 12);
    }

    private void PositionBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 12, area.Right - Width - 24);
        Top = Math.Max(area.Top + 12, area.Bottom - Height - 28);
    }

    private static void Animate(DependencyObject target, DependencyProperty property, double from, double to, int milliseconds, int delayMilliseconds = 0, EasingMode easingMode = EasingMode.EaseOut, int frameRate = NormalAnimationFrameRate)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            BeginTime = delayMilliseconds > 0 ? TimeSpan.FromMilliseconds(delayMilliseconds) : TimeSpan.Zero,
            EasingFunction = new CubicEase { EasingMode = easingMode },
        };
        Timeline.SetDesiredFrameRate(animation, frameRate);
        switch (target)
        {
            case UIElement element:
                element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
            case Animatable animatable:
                animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
        }
    }

    private static void FlashStatusChange(TaskCard card, TaskVisualStatus status)
    {
        switch (status)
        {
            case TaskVisualStatus.Error:
                FlashCard(card, 0.98, 1320, holdMilliseconds: 100);
                break;
            case TaskVisualStatus.Done:
                FlashCard(card, 0.92, 1160, holdMilliseconds: 80);
                break;
            default:
                FlashCard(card, 0.70, 820);
                break;
        }
    }

    private static void FlashCard(TaskCard card, double opacity, int milliseconds, int holdMilliseconds = 0)
    {
        card.Flash.BeginAnimation(OpacityProperty, null);
        card.Flash.Opacity = opacity;
        Animate(
            card.Flash,
            OpacityProperty,
            opacity,
            0,
            milliseconds,
            delayMilliseconds: holdMilliseconds,
            easingMode: EasingMode.EaseOut,
            frameRate: SlideAnimationFrameRate);
    }

    private void DebugLog(string eventName, object data)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (!ShouldWriteDebugLog(eventName, now))
            {
                return;
            }

            if ((now - _lastDebugRotationCheckUtc).TotalSeconds >= 30)
            {
                _lastDebugRotationCheckUtc = now;
                var fileInfo = new FileInfo(_debugLogPath);
                if (fileInfo.Exists && fileInfo.Length > 2_000_000)
                {
                    File.Move(_debugLogPath, IOPath.Combine(_stateDir, "debug.prev.jsonl"), overwrite: true);
                }
            }

            var payload = new
            {
                ts = now.ToString("o", CultureInfo.InvariantCulture),
                eventName,
                data
            };
            File.AppendAllText(_debugLogPath, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void DebugLogSessionEvent(string eventName, object data)
    {
        if (_suppressSessionReplayDebug && !_verboseLogging)
        {
            return;
        }

        DebugLog(eventName, data);
    }

    private bool ShouldWriteDebugLog(string eventName, DateTime now)
    {
        if (_verboseLogging)
        {
            return true;
        }

        var throttleSeconds = eventName switch
        {
            "rate_limits_update" => 30,
            "collapsed_strip_rebuild" => 10,
            "collapsed_dispose" => 10,
            "remove_task" => 2,
            "ack_remove" => 2,
            "ack_morph_idle" => 2,
            "completion_sound_preview_missing" => 2,
            _ => 0,
        };

        if (throttleSeconds <= 0)
        {
            return true;
        }

        if (_lastDebugLogByEvent.TryGetValue(eventName, out var last) &&
            (now - last).TotalSeconds < throttleSeconds)
        {
            return false;
        }

        _lastDebugLogByEvent[eventName] = now;
        return true;
    }

    private static bool ReadBooleanEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value is not null &&
               (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    private string GetThreadTitle(string threadId)
    {
        if (_threadTitles.TryGetValue(threadId, out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return threadId == "manual"
            ? Ui("手动状态", "Manual State")
            : $"{Ui("任务", "Task")} {threadId[..Math.Min(8, threadId.Length)]}";
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "";
        }

        var normalized = Regex.Replace(title.Trim(), @"\s+", "").ToUpperInvariant();
        return normalized.Length >= 3 ? normalized : "";
    }

    private static string? GetThreadIdFromPath(string path)
    {
        var match = ThreadIdRegex.Match(IOPath.GetFileName(path));
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static DateTime ReadTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out var prop) &&
            DateTime.TryParse(prop.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var value))
        {
            return value.ToUniversalTime();
        }

        return DateTime.UtcNow;
    }

    private static DateTime? ReadUnixSeconds(JsonElement payload, string property)
    {
        if (payload.TryGetProperty(property, out var prop) && prop.TryGetInt64(out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }

        return null;
    }

    private static DateTime FromUnixMilliseconds(long milliseconds)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
    }

    private void UpdateRateLimits(JsonElement rateLimits, DateTime observedAtUtc)
    {
        if (_rateLimits is not null && observedAtUtc < _rateLimits.ObservedAtUtc)
        {
            return;
        }

        if (!TryReadRateLimitWindow(rateLimits, "primary", out var fiveHour) ||
            !TryReadRateLimitWindow(rateLimits, "secondary", out var weekly))
        {
            return;
        }

        var planType = TryGetProperty(rateLimits, out var planProp, "plan_type", "planType")
            ? planProp.GetString()
            : null;
        _rateLimits = new RateLimitSnapshot(
            Math.Clamp(100.0 - weekly.UsedPercent, 0.0, 100.0),
            Math.Clamp(100.0 - fiveHour.UsedPercent, 0.0, 100.0),
            weekly.ResetsAt,
            fiveHour.ResetsAt,
            planType,
            observedAtUtc);
        DebugLog("rate_limits_update", new { weeklyRemaining = Math.Round(_rateLimits.WeeklyRemainingPercent), fiveHourRemaining = Math.Round(_rateLimits.FiveHourRemainingPercent), observedAtUtc });
    }

    private static bool TryReadRateLimitWindow(JsonElement rateLimits, string propertyName, out RateLimitWindow window)
    {
        window = default;
        if (!rateLimits.TryGetProperty(propertyName, out var element) ||
            !TryGetProperty(element, out var usedProp, "used_percent", "usedPercent") ||
            !TryReadDouble(usedProp, out var usedPercent))
        {
            return false;
        }

        var resetsAt = TryGetProperty(element, out var resetsProp, "resets_at", "resetsAt") &&
                       TryReadLong(resetsProp, out var resetsUnix)
            ? DateTimeOffset.FromUnixTimeSeconds(resetsUnix).UtcDateTime
            : (DateTime?)null;
        window = new RateLimitWindow(usedPercent, resetsAt);
        return true;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadLong(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static string GoalTaskId(string threadId)
    {
        return $"goal:{threadId}";
    }

    private static string FormatDuration(TaskState task)
    {
        var duration = task.Goal is not null
            ? task.Goal.Elapsed(DateTime.UtcNow)
            : task.Status == TaskVisualStatus.Working
            ? DateTime.UtcNow - task.StartedAt
            : task.Duration == TimeSpan.Zero && task.CompletedAt != default
                ? task.CompletedAt - task.StartedAt
                : task.Duration;

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private string GetDisplayTitle(TaskState task)
    {
        if (task.Status == TaskVisualStatus.Idle || task.TurnId == "idle")
        {
            return Ui("额度页", "Quota");
        }

        if (task.ThreadId == "manual" && string.Equals(task.Title, "手动状态", StringComparison.Ordinal))
        {
            return Ui("手动状态", "Manual State");
        }

        if (IsEnglishUi && task.Title.StartsWith("任务 ", StringComparison.Ordinal))
        {
            return "Task " + task.Title["任务 ".Length..];
        }

        if (!IsEnglishUi && task.Title.StartsWith("Task ", StringComparison.Ordinal))
        {
            return "任务 " + task.Title["Task ".Length..];
        }

        return task.Title;
    }

    private string GetDisplayMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "";
        }

        return message switch
        {
            "已中止" => Ui("已中止", "Aborted"),
            "待验收" => Ui("待验收", "Review"),
            "目标模式" => Ui("目标模式", "Goal Mode"),
            _ => message,
        };
    }

    private string GetStatusLabel(TaskState task)
    {
        if (task.Goal is not null)
        {
            return task.Goal.Status switch
            {
                "active" => Ui("目标运行中", "Goal Running"),
                "paused" => Ui("目标暂停", "Goal Paused"),
                "blocked" => Ui("目标受阻", "Goal Blocked"),
                "usage_limited" => Ui("用量受限", "Usage Limited"),
                "budget_limited" => Ui("预算受限", "Budget Limited"),
                "complete" => Ui("目标完成", "Goal Complete"),
                _ => Ui("目标模式", "Goal Mode"),
            };
        }

        return task.Status switch
        {
            TaskVisualStatus.Working => Ui("工作中", "Working"),
            TaskVisualStatus.Done => Ui("待验收", "Review"),
            TaskVisualStatus.Input => Ui("待输入", "Input"),
            TaskVisualStatus.Error => Ui("异常", "Error"),
            _ => Ui("额度页", "Quota")
        };
    }

    private static Brush CreateGlassBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.18),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(92, 7, 9, 15), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 16), 0.22));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(18, 24, 28, 38), 0.48));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 10), 0.70));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(136, 5, 7, 12), 1.00));
        return brush;
    }

    private static Brush CreateInnerRimBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0.25, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(24, 255, 255, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(7, 255, 255, 255), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.72));
        return brush;
    }

    private static Brush CreateSoftBadgeBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(214, 10, 13, 21), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 96), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(204, 5, 7, 12), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateExternalBalancePlateBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 8, 11, 18), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 178), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 5, 7, 12), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateExternalBalancePlateRimBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 132), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(58, 235, 240, 252), 0.52));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 104), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateBadgeRimBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 176), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(96, 235, 240, 252), 0.38));
        brush.GradientStops.Add(new GradientStop(WithAlpha(BlendColor(palette.Highlight, Color.FromRgb(232, 238, 250), 0.42), 152), 0.74));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 134), 1.00));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateBadgeTextBrush(StatusPalette palette)
    {
        return CreateFrozenBrush(BlendColor(palette.Highlight, Color.FromRgb(226, 232, 244), 0.48));
    }

    private static Brush CreateDurationTrailAuraBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 2, 4, 10), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 58), 0.16));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 88), 0.46));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 48), 0.70));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 2, 4, 10), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateDurationTrailCoreBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, palette.Accent.R, palette.Accent.G, palette.Accent.B), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 52), 0.20));
        brush.GradientStops.Add(new GradientStop(WithAlpha(BlendColor(palette.Highlight, Color.FromRgb(246, 249, 255), 0.28), 116), 0.52));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 46), 0.76));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, palette.Highlight.R, palette.Highlight.G, palette.Highlight.B), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateDurationTrailMask()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(210, 255, 255, 255), 0.16));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.48));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(190, 255, 255, 255), 0.76));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Grid CreateStatusBulb(StatusPalette palette, double size)
    {
        var bulb = new Grid
        {
            Width = size,
            Height = size,
            IsHitTestVisible = false,
        };

        var aura = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = CreateBulbAuraBrush(palette),
            Stroke = CreateFrozenBrush(WithAlpha(palette.Highlight, 128)),
            StrokeThickness = 0.9,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = palette.Highlight,
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.42,
            },
        };
        bulb.Children.Add(aura);

        var core = new Ellipse
        {
            Width = size * 0.58,
            Height = size * 0.58,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = CreateBulbCoreBrush(palette),
            Stroke = CreateFrozenBrush(Color.FromArgb(72, 255, 255, 255)),
            StrokeThickness = 0.6,
        };
        bulb.Children.Add(core);

        var glint = new Ellipse
        {
            Width = size * 0.22,
            Height = size * 0.16,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(size * 0.31, size * 0.26, 0, 0),
            Fill = CreateFrozenBrush(Color.FromArgb(162, 255, 255, 255)),
        };
        bulb.Children.Add(glint);

        return bulb;
    }

    private static Brush CreateCollapsedStripBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(224, 9, 11, 18), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 58), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(218, 7, 9, 15), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreatePanelRimBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 142), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(50, 255, 255, 255), 0.48));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 108), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateBulbWireBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 92), 0.22));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(96, 215, 220, 238), 0.50));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 76), 0.78));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateBulbAuraBrush(StatusPalette palette)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.46, 0.46),
            GradientOrigin = new Point(0.36, 0.30),
            RadiusX = 0.70,
            RadiusY = 0.72,
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(BlendColor(palette.Highlight, Color.FromRgb(246, 249, 255), 0.20), 210), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 128), 0.38));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 88), 0.68));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(34, 3, 5, 11), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateBulbCoreBrush(StatusPalette palette)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.42, 0.38),
            GradientOrigin = new Point(0.32, 0.26),
            RadiusX = 0.72,
            RadiusY = 0.76,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(240, 250, 252, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 214), 0.34));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 156), 0.76));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(18, 0, 0, 0), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateToggleButtonBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(218, 12, 15, 24), 0.00));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 64), 0.52));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(230, 4, 6, 12), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateToggleButtonRimBrush(StatusPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Highlight, 132), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(48, 255, 255, 255), 0.48));
        brush.GradientStops.Add(new GradientStop(WithAlpha(palette.Accent, 96), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateFlashBrush()
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.28, 0.38),
            GradientOrigin = new Point(0.18, 0.30),
            RadiusX = 0.92,
            RadiusY = 0.92,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(252, 255, 255, 255), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(168, 240, 246, 255), 0.36));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(34, 255, 255, 255), 1.00));
        return brush;
    }

    private static FontFamily FontForChinese()
    {
        return new FontFamily("Noto Sans SC, MiSans, MiSans-Regular, Microsoft YaHei UI, Segoe UI Variable Text, Segoe UI Variable, Segoe UI, sans-serif");
    }

    private static FontFamily FontForDuration()
    {
        return new FontFamily("Bahnschrift SemiCondensed, Bahnschrift SemiBold, Bahnschrift, Segoe UI Variable Display, sans-serif");
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Color BlendColor(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static StatusPalette GetPalette(TaskVisualStatus status)
    {
        return status switch
        {
            TaskVisualStatus.Working => new("#6E6AAE", "#9A93DE", "#E814151E"),
            TaskVisualStatus.Done => new("#628C78", "#9CC5AD", "#E8131A17"),
            TaskVisualStatus.Input => new("#B18A59", "#D5AD73", "#E81B1710"),
            TaskVisualStatus.Error => new("#A56870", "#D28A94", "#E81C1114"),
            _ => new("#64717D", "#8D98A5", "#E812151A"),
        };
    }

    private sealed class TaskState
    {
        public TaskState(string turnId, string threadId, string title, DateTime startedAt)
        {
            TurnId = turnId;
            ThreadId = threadId;
            Title = title;
            StartedAt = startedAt;
        }

        public string TurnId { get; }
        public string ThreadId { get; set; }
        public string Title { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public DateTime LastEventAt { get; set; } = DateTime.UtcNow;
        public TimeSpan Duration { get; set; }
        public bool ObservedLiveStart { get; set; }
        public TaskVisualStatus Status { get; set; } = TaskVisualStatus.Working;
        public bool WasUnread { get; set; }
        public DateTime? UnreadClearedAt { get; set; }
        public string Message { get; set; } = "";
        public GoalState? Goal { get; set; }
    }

    private sealed class GoalState
    {
        public GoalState(
            string threadId,
            string objective,
            string status,
            long? tokenBudget,
            long tokensUsed,
            TimeSpan timeUsed,
            DateTime createdAtUtc,
            DateTime updatedAtUtc)
        {
            ThreadId = threadId;
            Objective = objective;
            Status = status;
            TokenBudget = tokenBudget;
            TokensUsed = tokensUsed;
            TimeUsed = timeUsed;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
        }

        public string ThreadId { get; }
        public string Objective { get; }
        public string Status { get; }
        public long? TokenBudget { get; }
        public long TokensUsed { get; }
        public TimeSpan TimeUsed { get; }
        public DateTime CreatedAtUtc { get; }
        public DateTime UpdatedAtUtc { get; }
        public bool IsActive => Status == "active";

        public TimeSpan Elapsed(DateTime nowUtc)
        {
            var elapsed = TimeUsed;
            if (IsActive && nowUtc > UpdatedAtUtc)
            {
                elapsed += nowUtc - UpdatedAtUtc;
            }

            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }
    }

    private readonly record struct RateLimitWindow(double UsedPercent, DateTime? ResetsAt);

    private readonly record struct QuotaTooltipTargets(FrameworkElement Track, FrameworkElement Center, double RingSize, double RingStroke);

    private sealed record ExternalBalanceProviderConfig(
        string Id,
        string BaseUrl,
        string? Kind,
        string SystemToken,
        string? UserId,
        IReadOnlyList<string> Endpoints,
        double? BaselineAmount,
        double? LastObservedAmount,
        double? ConsumedAmount);

    private sealed record ExternalBalanceEditorValues(
        string ShengshengDisplayName,
        string ShengshengUserId,
        string ShengshengToken,
        string DeepkeyDisplayName,
        string DeepkeyUserId,
        string DeepkeyToken);

    private readonly record struct ExternalBalanceSnapshot(string DisplayText, double? Amount, string CurrencyPrefix, string? SecondaryText);

    private sealed record ShengshengStatusConfig(
        double QuotaPerUnit,
        string QuotaDisplayType,
        double UsdExchangeRate,
        string CustomCurrencySymbol,
        double CustomCurrencyExchangeRate);

    private sealed record CompletionSoundChoice(string Id, string DisplayNameZh, string DisplayNameEn, string RelativePath);

    private sealed record RateLimitSnapshot(
        double WeeklyRemainingPercent,
        double FiveHourRemainingPercent,
        DateTime? WeeklyResetsAt,
        DateTime? FiveHourResetsAt,
        string? PlanType,
        DateTime ObservedAtUtc);

    private readonly record struct BadgeLayers(Border Fill, ShapePath Rim);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct POINT
    {
        public readonly int x;
        public readonly int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MSLLHOOKSTRUCT
    {
        public readonly POINT pt;
        public readonly uint mouseData;
        public readonly uint flags;
        public readonly uint time;
        public readonly IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private sealed class TaskCard
    {
        public TaskCard(Border root, Border surface, Border surfaceGlow, TranslateTransform translate, Canvas pixels, Border glass, Grid edgeHighlights, Border flash, Grid contentGrid, Grid quotaPanel, ShapePath weeklyQuotaArc, TextBlock weeklyQuotaText, ShapePath fiveHourQuotaArc, TextBlock fiveHourQuotaText, TextBlock shengshengBalanceText, TextBlock shengshengBalanceArrowText, TextBlock shengshengBalanceDeltaText, TextBlock deepkeyBalanceText, TextBlock deepkeyBalanceArrowText, TextBlock deepkeyBalanceDeltaText, Border quotaPinButton, Border quotaResetButton, ShapePath quotaPinHead, ShapePath quotaPinNeedle, TextBlock title, TextBlock message, Border badge, TextBlock badgeText, Grid durationTrail, Border durationAura, Border durationCore, TextBlock duration, Border toggleButton)
        {
            Root = root;
            Surface = surface;
            SurfaceGlow = surfaceGlow;
            Translate = translate;
            Pixels = pixels;
            Glass = glass;
            EdgeHighlights = edgeHighlights;
            Flash = flash;
            ContentGrid = contentGrid;
            QuotaPanel = quotaPanel;
            WeeklyQuotaArc = weeklyQuotaArc;
            WeeklyQuotaText = weeklyQuotaText;
            FiveHourQuotaArc = fiveHourQuotaArc;
            FiveHourQuotaText = fiveHourQuotaText;
            ShengshengBalanceText = shengshengBalanceText;
            ShengshengBalanceArrowText = shengshengBalanceArrowText;
            ShengshengBalanceDeltaText = shengshengBalanceDeltaText;
            DeepkeyBalanceText = deepkeyBalanceText;
            DeepkeyBalanceArrowText = deepkeyBalanceArrowText;
            DeepkeyBalanceDeltaText = deepkeyBalanceDeltaText;
            QuotaPinButton = quotaPinButton;
            QuotaResetButton = quotaResetButton;
            QuotaPinHead = quotaPinHead;
            QuotaPinNeedle = quotaPinNeedle;
            Title = title;
            Message = message;
            Badge = badge;
            BadgeText = badgeText;
            DurationTrail = durationTrail;
            DurationAura = durationAura;
            DurationCore = durationCore;
            Duration = duration;
            ToggleButton = toggleButton;
        }

        public Border Root { get; }
        public Border Surface { get; }
        public Border SurfaceGlow { get; }
        public TranslateTransform Translate { get; }
        public Canvas Pixels { get; }
        public Border Glass { get; }
        public Grid EdgeHighlights { get; }
        public Border Flash { get; }
        public Grid ContentGrid { get; }
        public Grid QuotaPanel { get; }
        public ShapePath WeeklyQuotaArc { get; }
        public TextBlock WeeklyQuotaText { get; }
        public ShapePath FiveHourQuotaArc { get; }
        public TextBlock FiveHourQuotaText { get; }
        public TextBlock ShengshengBalanceText { get; }
        public TextBlock ShengshengBalanceArrowText { get; }
        public TextBlock ShengshengBalanceDeltaText { get; }
        public TextBlock DeepkeyBalanceText { get; }
        public TextBlock DeepkeyBalanceArrowText { get; }
        public TextBlock DeepkeyBalanceDeltaText { get; }
        public Border QuotaPinButton { get; }
        public Border QuotaResetButton { get; }
        public ShapePath QuotaPinHead { get; }
        public ShapePath QuotaPinNeedle { get; }
        public TextBlock Title { get; }
        public TextBlock Message { get; }
        public Border Badge { get; }
        public TextBlock BadgeText { get; }
        public Grid DurationTrail { get; }
        public Border DurationAura { get; }
        public Border DurationCore { get; }
        public TextBlock Duration { get; }
        public Border ToggleButton { get; }
        public List<Rectangle> PixelRects { get; } = new();
        public List<bool> PixelActiveStates { get; } = new();
        public Brush PixelActiveBrush { get; set; } = CreateFrozenBrush(Color.FromRgb(154, 147, 222));
        public Brush PixelInactiveBrush { get; set; } = CreateFrozenBrush(Color.FromRgb(110, 106, 174));
        public StatusPalette Palette { get; set; } = GetPalette(TaskVisualStatus.Idle);
        public TaskVisualStatus Status { get; set; } = TaskVisualStatus.Idle;
        public double? FiveHourQuotaRemainingPercent { get; set; }
        public double TargetY { get; set; } = double.NaN;
        public bool PaletteInitialized { get; set; }
        public bool ForcePixelRefresh { get; set; } = true;
    }

    private enum TaskVisualStatus
    {
        Idle,
        Working,
        Done,
        Input,
        Error
    }

    private enum UiLanguage
    {
        Chinese,
        English
    }

    private sealed record StatusPalette(Color Accent, Color Highlight, Color Surface)
    {
        public StatusPalette(string accent, string highlight, string surface)
            : this((Color)ColorConverter.ConvertFromString(accent),
                   (Color)ColorConverter.ConvertFromString(highlight),
                   (Color)ColorConverter.ConvertFromString(surface))
        {
        }
    }
}
