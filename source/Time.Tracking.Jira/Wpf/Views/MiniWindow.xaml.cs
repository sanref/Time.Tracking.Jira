using Time.Tracking.Jira.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Time.Tracking.Jira.Wpf.Views
{
    /// <summary>
    /// The always-on-top strip: one line with the entry being tracked, its clock and
    /// pause/resume, and nothing else. Opened from the focus button; the tracker window hides
    /// while it is up and shows itself again when it closes.
    /// </summary>
    /// <remarks>
    /// Always <see cref="Window.Topmost"/>, regardless of the always-on-top setting: it has no
    /// taskbar button, so a strip that ended up behind another window could not be reached.
    /// </remarks>
    public partial class MiniWindow : Window
    {
        // Distance from the corner of the work area the first time it is opened
        private const double DefaultMargin = 16;

        internal MiniWindow(TimeEntryViewModel entry)
        {
            InitializeComponent();

            DataContext = entry;

            RestorePlacement();
        }

        private void Surface_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        /// <summary>
        /// Double-click anywhere goes back to the full window. Closing is all this does: the
        /// tracker window owns the strip's lifetime and brings itself back.
        /// </summary>
        private void Surface_Restore(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        #region placement

        /// <summary>
        /// Opens where it was left. The position goes in the shared settings object and is
        /// written to disk with everything else when the application closes.
        /// </summary>
        private void RestorePlacement()
        {
            Settings settings = Settings.Instance;

            if (IsOnScreen(settings.MiniLeft, settings.MiniTop))
            {
                Left = settings.MiniLeft;
                Top = settings.MiniTop;
                return;
            }

            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - DefaultMargin;
            Top = workArea.Top + DefaultMargin;
        }

        private void SavePlacement()
        {
            Settings settings = Settings.Instance;

            settings.MiniLeft = Left;
            settings.MiniTop = Top;
        }

        /// <summary>
        /// A position saved on a monitor that is no longer connected would put the strip out of
        /// reach, and it has no taskbar button to get it back with. Both zeroes means it has
        /// never been opened, which docks it in the default corner.
        /// </summary>
        private bool IsOnScreen(double left, double top)
        {
            if (left == 0 && top == 0)
                return false;

            Rect screens = new Rect(
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

            return screens.Contains(new Rect(left, top, Width, Height));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SavePlacement();
            base.OnClosing(e);
        }

        #endregion
    }
}
