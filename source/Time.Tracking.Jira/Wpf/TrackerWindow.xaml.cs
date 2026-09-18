using Time.Tracking.Jira.Logging;
using Time.Tracking.Jira.Wpf.Infrastructure;
using Time.Tracking.Jira.Wpf.ViewModels;
using Time.Tracking.Jira.Wpf.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Time.Tracking.Jira.Wpf
{
    /// <summary>
    /// Chrome around whichever view is selected: tray behaviour, always-on-top, the
    /// single-instance message and the mini window. Everything about time and Jira lives in
    /// the view model, which all four layouts share.
    /// </summary>
    public partial class TrackerWindow : Window
    {
        private readonly Settings settings;
        private readonly DialogService dialogs;
        private readonly LedgerViewModel viewModel;

        private System.Windows.Forms.NotifyIcon notifyIcon;

        private MiniWindow miniWindow;

        // The application is on its way out: closing the strip must not pull this window back
        private bool shuttingDown;

        // The view whose metrics the window is currently sized to
        private ViewMode appliedViewMode;

        // Size the user last left each view at, restored on the next visit
        private readonly Dictionary<ViewMode, Size> savedSizes;

        public TrackerWindow()
        {
            // Program.Main has already loaded the settings
            settings = Settings.Instance;

            InitializeComponent();

            Title = AppInfo.Title;

            dialogs = new DialogService();
            viewModel = new LedgerViewModel(settings, dialogs);
            viewModel.FocusRequested += OnFocusRequested;
            viewModel.SettingsApplied += OnSettingsApplied;
            viewModel.ViewModeChanged += OnViewModeChanged;

            DataContext = viewModel;
            Topmost = settings.AlwaysOnTop;

            savedSizes = ParseSizes(settings.ViewWindowSizes);
            ApplyViewMetrics(true);
        }

        #region size

        /// <summary>
        /// The smallest size a layout still renders at, found by shrinking each one until it
        /// clipped. The floor follows the view rather than taking the widest one for
        /// everybody: the Grid needs two columns and a tall header, the Cards get by on far
        /// less. The opening height is the floor for Focus, and taller where the point of the
        /// layout is seeing several entries at once.
        /// </summary>
        private static void MetricsFor(ViewMode mode, out double minWidth, out double minHeight, out double openHeight)
        {
            switch (mode)
            {
                // 560 fit the header before "Log all" joined it; re-measured the same way,
                // by shrinking until the TOTAL time clipped.
                case ViewMode.Cards:
                    minWidth = 640; minHeight = 320; openHeight = 640;
                    break;
                case ViewMode.Focus:
                    minWidth = 620; minHeight = 420; openHeight = 420;
                    break;
                case ViewMode.Grid:
                    // tall enough for two rows of cells, so four are in view
                    minWidth = 900; minHeight = 360; openHeight = 640;
                    break;
                default:
                    minWidth = 680; minHeight = 320; openHeight = 640;
                    break;
            }
        }

        private void ApplyViewMetrics(bool resize)
        {
            double minWidth, minHeight, openHeight;
            MetricsFor(viewModel.ViewMode, out minWidth, out minHeight, out openHeight);

            MinWidth = minWidth;
            MinHeight = minHeight;
            appliedViewMode = viewModel.ViewMode;

            if (!resize)
            {
                // Same view: only pull the window up if it now sits below the floor
                if (Width < minWidth) Width = minWidth;
                if (Height < minHeight) Height = minHeight;
                return;
            }

            Size remembered;
            if (savedSizes.TryGetValue(viewModel.ViewMode, out remembered))
            {
                Width = Math.Max(remembered.Width, minWidth);
                Height = Math.Max(remembered.Height, minHeight);
                return;
            }

            Width = minWidth;
            Height = openHeight;
        }

        /// <summary>Keeps the size of the view being left, so returning to it restores it.</summary>
        private void RememberSize()
        {
            // A maximized or minimized window would store the screen, not the user's choice
            if (WindowState != WindowState.Normal)
                return;

            savedSizes[appliedViewMode] = new Size(Width, Height);
        }

        private static Dictionary<ViewMode, Size> ParseSizes(string raw)
        {
            Dictionary<ViewMode, Size> sizes = new Dictionary<ViewMode, Size>();
            if (string.IsNullOrEmpty(raw))
                return sizes;

            foreach (string pair in raw.Split(';'))
            {
                string[] halves = pair.Split('=');
                if (halves.Length != 2)
                    continue;

                string[] size = halves[1].Split('x');
                if (size.Length != 2)
                    continue;

                ViewMode mode;
                int width, height;

                if (!TryParseMode(halves[0], out mode) ||
                    !int.TryParse(size[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) ||
                    !int.TryParse(size[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height))
                    continue;

                sizes[mode] = new Size(width, height);
            }

            return sizes;
        }

        private static bool TryParseMode(string name, out ViewMode mode)
        {
            mode = ViewMode.Ledger;

            foreach (ViewMode candidate in Enum.GetValues(typeof(ViewMode)))
            {
                if (!string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                mode = candidate;
                return true;
            }

            return false;
        }

        private static string FormatSizes(Dictionary<ViewMode, Size> sizes)
        {
            StringBuilder text = new StringBuilder();

            foreach (KeyValuePair<ViewMode, Size> entry in sizes)
            {
                if (text.Length > 0)
                    text.Append(';');

                text.AppendFormat(CultureInfo.InvariantCulture, "{0}={1}x{2}",
                    entry.Key, (int)entry.Value.Width, (int)entry.Value.Height);
            }

            return text.ToString();
        }

        #endregion

        #region window lifetime

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // The handle exists from here on: the modal dialogs can be owned, and the
            // single-instance broadcast can be listened for.
            dialogs.Owner = this;

            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null)
                source.AddHook(WndProcHook);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            viewModel.Initialize();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            shuttingDown = true;
            CloseMiniWindow();

            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                notifyIcon = null;
            }

            // Has to happen before Shutdown, which is what writes the settings out
            RememberSize();
            settings.ViewWindowSizes = FormatSizes(savedSizes);

            viewModel.Shutdown();
        }

        private void OnSettingsApplied(object sender, EventArgs e)
        {
            Topmost = settings.AlwaysOnTop;

            if (!settings.MinimizeToTray && notifyIcon != null)
                notifyIcon.Visible = false;

            // Only a real view change resizes; accepting Settings otherwise must not snap a
            // window the user had grown back down to the floor.
            bool viewChanged = viewModel.ViewMode != appliedViewMode;

            if (viewChanged)
                RememberSize();

            ApplyViewMetrics(viewChanged);
        }

        /// <summary>The footer's quick switcher — always a real change, so always resize:
        /// remember where the view being left was, then restore or default the new one.</summary>
        private void OnViewModeChanged(object sender, EventArgs e)
        {
            RememberSize();
            ApplyViewMetrics(true);
        }

        /// <summary>
        /// The 1..9 keys start row N. They are handled here rather than as window
        /// <c>InputBindings</c> because a <see cref="KeyBinding"/> to a plain
        /// <see cref="System.Windows.Input.ICommand"/> marks the key event handled the moment
        /// its gesture matches — run or not — which over the Ledger's inline cell editor means
        /// a digit typed into an issue-key search never reaches the field. The Ledger is the
        /// one view with that editor, so the shortcut is skipped there and kept everywhere
        /// else. Bare digits only, matching the KeyBindings this replaced.
        /// </summary>
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (e.Key < Key.D1 || e.Key > Key.D9)
                return;

            if (viewModel.ViewMode == ViewMode.Ledger)
                return;

            viewModel.StartByIndexCommand.Execute((e.Key - Key.D1 + 1).ToString(CultureInfo.InvariantCulture));
            e.Handled = true;
        }

        #endregion

        #region tray

        private void Window_StateChanged(object sender, EventArgs e)
        {
            // Mono for MacOSX and Linux do not implement the notify icon, so ignore this
            // feature if we are not running on Windows
            if (!CrossPlatformHelpers.IsWindowsEnvironment())
                return;

            if (!settings.MinimizeToTray)
                return;

            if (WindowState == WindowState.Minimized)
            {
                EnsureNotifyIcon();
                notifyIcon.Visible = true;
                Hide();
            }
            else if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
            }
        }

        private void EnsureNotifyIcon()
        {
            if (notifyIcon != null)
                return;

            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Text = AppInfo.Title;
            // El icono se saca del ejecutable, no del ensamblado: desde .NET son dos archivos
            // distintos (Time.Tracking.Jira.exe es un apphost nativo y el ensamblado gestionado es
            // Time.Tracking.Jira.dll, que no tiene recursos de icono). Assembly.Location apuntaria
            // al dll y la bandeja quedaria sin icono.
            notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            notifyIcon.Click += (s, e) => RestoreFromTray();
        }

        private void RestoreFromTray()
        {
            // Coming back from the tray while the strip is up means leaving the strip. It is a
            // no-op when this is called from the strip's own Closed handler.
            CloseMiniWindow();

            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>A second instance was launched: it asks this one to come to the front.</summary>
        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_SHOWME)
                RestoreFromTray();

            return IntPtr.Zero;
        }

        #endregion

        #region mini window

        /// <summary>
        /// Swaps this window for the strip. The entry is captured here rather than read from
        /// the view model each time: the strip keeps showing —and pausing— the same one, and
        /// <see cref="LedgerViewModel.RunningEntry"/> is null while nothing runs.
        /// </summary>
        private void OnFocusRequested(object sender, EventArgs e)
        {
            if (miniWindow != null)
            {
                miniWindow.Activate();
                return;
            }

            TimeEntryViewModel entry = viewModel.RunningEntry;
            if (entry == null)
                return;

            Logger.Instance.Log(string.Format("Opening the mini window for {0}", entry.TimerKey));

            miniWindow = new MiniWindow(entry);
            miniWindow.Closed += MiniWindow_Closed;

            // Hiding this window is what leaves the strip alone on screen, and it must not
            // happen until the strip has actually painted: ContentRendered is the first event
            // that guarantees a frame reached the screen. Hiding right after Show() left the
            // strip's first frame unpainted on some machines — only the region redrawn by the
            // ticking clock ever appeared, over a transparent window.
            miniWindow.ContentRendered += MiniWindow_FirstFrame;

            miniWindow.Show();
            miniWindow.Activate();
        }

        private void MiniWindow_FirstFrame(object sender, EventArgs e)
        {
            MiniWindow strip = (MiniWindow)sender;
            strip.ContentRendered -= MiniWindow_FirstFrame;

            Logger.Instance.Log(string.Format(
                "Mini window rendered at {0},{1} ({2}x{3}); hiding the tracker window",
                strip.Left, strip.Top, strip.ActualWidth, strip.ActualHeight));

            // The strip has no taskbar button, so the tray icon is the way back if it ends up
            // somewhere unexpected — without it, hiding this window strands the application.
            if (CrossPlatformHelpers.IsWindowsEnvironment())
            {
                EnsureNotifyIcon();
                notifyIcon.Visible = true;
            }

            Hide();
        }

        private void MiniWindow_Closed(object sender, EventArgs e)
        {
            miniWindow = null;

            // Closing the strip is how you come back — unless the whole application is
            // closing, in which case there is nothing to come back to.
            if (shuttingDown)
                return;

            if (notifyIcon != null && !settings.MinimizeToTray)
                notifyIcon.Visible = false;

            RestoreFromTray();
        }

        private void CloseMiniWindow()
        {
            if (miniWindow != null)
                miniWindow.Close();
        }

        #endregion
    }
}
