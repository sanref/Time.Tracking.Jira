using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Time.Tracking.Jira.Wpf.Infrastructure
{
    /// <summary>
    /// A button that drops its own <see cref="FrameworkElement.ContextMenu"/> on a left
    /// click, placed under the button. Used for the per-row "more actions" menu, where the
    /// usual right-click affordance would go unnoticed.
    /// </summary>
    public class MenuButton : Button
    {
        public MenuButton()
        {
            // The menu is not in the visual tree, so its DataContext does not always follow
            // the row's on its own — hand it over on every open, whichever way it is opened.
            ContextMenuOpening += (sender, e) => SyncMenuDataContext();
        }

        protected override void OnClick()
        {
            base.OnClick();

            ContextMenu menu = ContextMenu;
            if (menu == null)
                return;

            SyncMenuDataContext();
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void SyncMenuDataContext()
        {
            if (ContextMenu != null)
                ContextMenu.DataContext = DataContext;
        }
    }
}
