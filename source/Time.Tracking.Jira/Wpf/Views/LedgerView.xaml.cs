using Time.Tracking.Jira.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Time.Tracking.Jira.Wpf.Views
{
    public partial class LedgerView : UserControl
    {
        public LedgerView()
        {
            InitializeComponent();

            // Caught here, at the top of the tunnel, so Enter and Escape are dealt with before
            // the combo's own search handler — which swallows Enter whenever a list row is
            // highlighted — ever sees them.
            AddHandler(PreviewKeyDownEvent, new KeyEventHandler(Edit_PreviewKeyDown));
        }

        private LedgerViewModel Model
        {
            get { return DataContext as LedgerViewModel; }
        }

        private static TimeEntryViewModel EntryOf(object sender)
        {
            FrameworkElement element = sender as FrameworkElement;
            return element == null ? null : element.DataContext as TimeEntryViewModel;
        }

        /// <summary>Double-clicking the parent or subtask cell swaps it for its combo box.</summary>
        private void Cell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
                return;

            TimeEntryViewModel entry = EntryOf(sender);
            if (entry == null || Model == null)
                return;

            Model.BeginEdit(entry);
            e.Handled = true;
        }

        /// <summary>
        /// The combos live in the row template all along, just collapsed, so the moment they
        /// are shown is when they have to pick up the row's value and take focus.
        /// </summary>
        private void Edit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            TimeEntryViewModel entry = EntryOf(sender);
            if (entry == null)
                return;

            if (!combo.IsVisible)
            {
                combo.IsDropDownOpen = false;
                return;
            }

            // Only ever set Text, never SelectedItem: with SelectedItem left null, reloading
            // the subtask list does not wipe what the row already shows.
            combo.Text = combo.Name == "ParentEdit" ? entry.ParentKey : entry.SubtaskKey;

            if (combo.Name == "ParentEdit")
                combo.Focus();
        }

        /// <summary>
        /// Enter and Escape while a cell's combo is open. Enter commits whatever the combo
        /// holds and walks parent → subtask, the same hop Tab makes across the cells; on the
        /// subtask it closes the editor. Escape closes it without committing. Tab is left to
        /// WPF and keeps working.
        /// </summary>
        private void Edit_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Escape)
                return;

            ComboBox combo = EditComboOf(e.OriginalSource as DependencyObject);
            if (combo == null || Model == null)
                return;

            TimeEntryViewModel entry = EntryOf(combo);
            if (entry == null)
                return;

            if (e.Key == Key.Escape)
            {
                // First press closes the suggestion list; a second one drops the edit.
                if (combo.IsDropDownOpen)
                    combo.IsDropDownOpen = false;
                else
                    Model.EndEdit(entry);

                e.Handled = true;
                return;
            }

            // Enter: whatever the list is doing, take what the combo holds and move on.
            combo.IsDropDownOpen = false;
            Apply(combo, entry);

            ComboBox subtask = combo.Name == "ParentEdit" ? SiblingCombo(combo, "SubtaskEdit") : null;
            if (subtask != null)
            {
                // Applying the parent may have cleared the subtask; the combo's text was set
                // when it first showed and would otherwise keep the old key on screen.
                subtask.Text = entry.SubtaskKey ?? "";
                subtask.Focus();
            }
            else
            {
                Model.EndEdit(entry);
            }

            e.Handled = true;
        }

        /// <summary>The parent/subtask edit combo the key came from, walking out from the focused part.</summary>
        private static ComboBox EditComboOf(DependencyObject source)
        {
            while (source != null)
            {
                ComboBox combo = source as ComboBox;
                if (combo != null && (combo.Name == "ParentEdit" || combo.Name == "SubtaskEdit"))
                    return combo;

                Visual visual = source as Visual;
                source = visual == null ? null : VisualTreeHelper.GetParent(visual);
            }

            return null;
        }

        /// <summary>The other edit combo in the same row, found by climbing to a shared parent.</summary>
        private static ComboBox SiblingCombo(DependencyObject from, string name)
        {
            for (DependencyObject node = from; node != null; node = LogicalTreeHelper.GetParent(node))
            {
                DependencyObject match = LogicalTreeHelper.FindLogicalNode(node, name);
                if (match is ComboBox)
                    return (ComboBox)match;
            }

            return null;
        }

        private void ParentEdit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            IssueItem picked = combo.SelectedItem as IssueItem;
            TimeEntryViewModel entry = EntryOf(sender);

            if (picked == null || entry == null || Model == null)
                return;

            Model.SetParent(entry, picked.Key, picked.Summary);
        }

        private void ParentEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            TimeEntryViewModel entry = EntryOf(sender);

            if (entry == null || Model == null || !combo.IsVisible)
                return;

            // Covers a key typed by hand, which never raises SelectionChanged
            string typed = (combo.Text ?? "").Trim();
            if (typed.Length > 0)
                Model.SetParent(entry, typed, null);
        }

        private void SubtaskEdit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            IssueItem picked = combo.SelectedItem as IssueItem;
            TimeEntryViewModel entry = EntryOf(sender);

            if (picked == null || entry == null || Model == null)
                return;

            Model.SetSubtask(entry, picked.Key, picked.Summary);
        }

        private void SubtaskEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            TimeEntryViewModel entry = EntryOf(sender);

            if (entry == null || Model == null || !combo.IsVisible)
                return;

            string typed = (combo.Text ?? "").Trim();
            if (typed.Length > 0 && typed != entry.SubtaskKey)
                Model.SetSubtask(entry, typed, SummaryFor(entry, typed));
        }

        private void Apply(ComboBox combo, TimeEntryViewModel entry)
        {
            string typed = (combo.Text ?? "").Trim();
            if (typed.Length == 0)
                return;

            if (combo.Name == "ParentEdit")
                Model.SetParent(entry, typed, null);
            else
                Model.SetSubtask(entry, typed, SummaryFor(entry, typed));
        }

        /// <summary>The title that goes with a subtask key, empty when it was typed by hand.</summary>
        private static string SummaryFor(TimeEntryViewModel entry, string key)
        {
            foreach (IssueItem item in entry.Subtasks)
                if (item.Key == key)
                    return item.Summary;

            return "";
        }
    }
}
