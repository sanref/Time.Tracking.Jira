using Time.Tracking.Jira.Wpf.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Time.Tracking.Jira.Wpf.Infrastructure
{
    /// <summary>
    /// Asks Jira for issues matching what was typed, adding whatever it finds to the list the
    /// combo is bound to, and calls back once it has — whether it found anything or not.
    /// </summary>
    public delegate void IssueLookup(string text, Action onDone);

    /// <summary>
    /// Turns an editable issue combo into a search field: what you type filters the drop-down
    /// to the issues whose key contains it, anywhere in the key, and the list opens as you
    /// type.
    ///
    /// It replaces WPF's built-in <see cref="TextSearch"/>, which only ever jumped to the key
    /// that <em>starts</em> with what was typed — so "6573" or "T-698" matched nothing, and
    /// the parent list, which holds every issue assigned to you, could only be walked by
    /// scrolling.
    ///
    /// The list itself only holds what was loaded into it. Where an <see cref="IssueLookup"/>
    /// is attached the search also goes out to Jira, every time, so an issue that is closed,
    /// or someone else's, is found by typing its key just the same.
    /// </summary>
    public static class IssueSearch
    {
        // Long enough that a key typed straight through is one query, not one per character
        private const int LookupDelayMs = 350;

        // Two characters of a key are not a search, they are the start of one
        private const int MinLookupChars = 3;

        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(IssueSearch), new PropertyMetadata(false, OnEnabledChanged));

        /// <summary>Where the search goes when the list itself has nothing. Optional.</summary>
        public static readonly DependencyProperty LookupProperty = DependencyProperty.RegisterAttached(
            "Lookup", typeof(IssueLookup), typeof(IssueSearch), new PropertyMetadata(null));

        // The filter moves the editor's text around behind the user's back, and every one of
        // those moves comes back as a TextChanged. This is what tells the two apart.
        private static readonly DependencyProperty SuppressedProperty = DependencyProperty.RegisterAttached(
            "Suppressed", typeof(bool), typeof(IssueSearch), new PropertyMetadata(false));

        // Set by a keystroke, consumed by the TextChanged it causes: text that the code behind
        // writes into the field — a row opening its editor, an item picked from the list — is
        // not a search and must not pop the list open.
        private static readonly DependencyProperty TypingProperty = DependencyProperty.RegisterAttached(
            "Typing", typeof(bool), typeof(IssueSearch), new PropertyMetadata(false));

        private static readonly DependencyProperty LookupTimerProperty = DependencyProperty.RegisterAttached(
            "LookupTimer", typeof(DispatcherTimer), typeof(IssueSearch), new PropertyMetadata(null));

        // Last text sent to Jira, so a search that came back with nothing is not sent again on
        // the very next pass — which is what re-filtering after each answer would otherwise do.
        private static readonly DependencyProperty LookedUpProperty = DependencyProperty.RegisterAttached(
            "LookedUp", typeof(string), typeof(IssueSearch), new PropertyMetadata(null));

        public static void SetEnabled(DependencyObject element, bool value)
        {
            element.SetValue(EnabledProperty, value);
        }

        public static bool GetEnabled(DependencyObject element)
        {
            return (bool)element.GetValue(EnabledProperty);
        }

        public static void SetLookup(DependencyObject element, IssueLookup value)
        {
            element.SetValue(LookupProperty, value);
        }

        public static IssueLookup GetLookup(DependencyObject element)
        {
            return (IssueLookup)element.GetValue(LookupProperty);
        }

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ComboBox combo = d as ComboBox;
            if (combo == null)
                return;

            if ((bool)e.NewValue)
            {
                // The built-in autocomplete would keep rewriting the text we are filtering on,
                // and the drop-down has to survive the editor keeping focus while typing.
                combo.IsTextSearchEnabled = false;
                combo.StaysOpenOnEdit = true;
                combo.IsSynchronizedWithCurrentItem = false;

                combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));
                combo.PreviewTextInput += OnPreviewTextInput;
                combo.PreviewKeyDown += OnPreviewKeyDown;
                combo.DropDownClosed += OnDropDownClosed;
                combo.IsKeyboardFocusWithinChanged += OnIsKeyboardFocusWithinChanged;
            }
            else
            {
                combo.RemoveHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));
                combo.PreviewTextInput -= OnPreviewTextInput;
                combo.PreviewKeyDown -= OnPreviewKeyDown;
                combo.DropDownClosed -= OnDropDownClosed;
                combo.IsKeyboardFocusWithinChanged -= OnIsKeyboardFocusWithinChanged;

                StopLookup(combo);
                ClearFilter(combo);
            }
        }

        #region input

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            ((ComboBox)sender).SetValue(TypingProperty, true);
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;

            // PreviewTextInput does not see these, and they change the search just as much:
            // the two erasing keys, and paste, cut and undo.
            bool edited = e.Key == Key.Back || e.Key == Key.Delete ||
                (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.V || e.Key == Key.X || e.Key == Key.Z));

            if (edited)
            {
                combo.SetValue(TypingProperty, true);
                return;
            }

            if (e.Key != Key.Enter || !combo.IsDropDownOpen)
                return;

            // Enter belongs to whoever owns the field — the ledger row commits and moves on,
            // the dialog accepts. Closing the list here, rather than letting the ComboBox
            // commit a highlighted item over the text, is what keeps what was typed intact.
            // The one case it is not ours is a list entry the user arrowed onto: that Enter
            // picks it, and goes no further.
            bool picked = combo.SelectedItem != null;
            combo.IsDropDownOpen = false;
            e.Handled = picked;
        }

        private static void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;

            bool typed = (bool)combo.GetValue(TypingProperty);
            combo.SetValue(TypingProperty, false);

            if (!typed || (bool)combo.GetValue(SuppressedProperty))
                return;

            ApplyFilter(combo, e.OriginalSource as TextBox);
        }

        private static void OnDropDownClosed(object sender, EventArgs e)
        {
            // The search lives as long as the list it filters: reopening with the arrow, or
            // coming back to a row later, offers everything again. WPF raises this one off
            // the dispatcher, so a list that has since reopened is a newer search than this.
            ComboBox combo = (ComboBox)sender;
            if (!combo.IsDropDownOpen)
                ClearFilter(combo);
        }

        private static void OnIsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
                return;

            ComboBox combo = (ComboBox)sender;
            StopLookup(combo);
            ClearFilter(combo);
        }

        #endregion

        #region filtering

        /// <summary>
        /// Filters through <c>Items</c>, which is the view of whatever the combo is bound to.
        /// The ledger rows share one collection, so they share that view — harmless here,
        /// because only one row edits at a time and the filter is dropped the moment the
        /// list closes or the field loses focus.
        /// </summary>
        private static void ApplyFilter(ComboBox combo, TextBox editor)
        {
            if (!combo.Items.CanFilter)
                return;

            if (editor == null)
                editor = Editor(combo);

            string text = editor == null ? (combo.Text ?? "") : editor.Text;
            int caret = editor == null ? 0 : editor.CaretIndex;

            string[] terms = IssueItem.Terms(text);

            combo.SetValue(SuppressedProperty, true);
            try
            {
                combo.Items.Filter = terms.Length == 0
                    ? null
                    : new Predicate<object>(item => item is IssueItem && ((IssueItem)item).Matches(terms));

                RestoreEditor(editor, text, caret);
            }
            finally
            {
                combo.SetValue(SuppressedProperty, false);
            }

            // An empty list is worse than no list: it covers the field with a blank box
            bool empty = combo.Items.IsEmpty;
            combo.IsDropDownOpen = !empty;

            // Dropping the list open makes the ComboBox select the whole editor, and the next
            // keystroke would then replace the search instead of extending it. WPF does that
            // both inline and on a dispatcher callback of its own, so undo it in both passes.
            CollapseSelection(editor, text, caret);
            combo.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(() => CollapseSelection(editor, text, caret)));

            // Whether or not the list had something, the search goes on to Jira: the list
            // only holds the issues assigned to you, and the one being looked for is often
            // someone else's, or closed, or in a project you have never been assigned in.
            string query = text.Trim();
            if (query.Length >= MinLookupChars)
                ScheduleLookup(combo, query);
            else
                StopLookup(combo);
        }

        private static void ClearFilter(ComboBox combo)
        {
            if (!combo.Items.CanFilter || combo.Items.Filter == null)
                return;

            TextBox editor = Editor(combo);
            string text = editor == null ? null : editor.Text;
            int caret = editor == null ? 0 : editor.CaretIndex;

            combo.SetValue(SuppressedProperty, true);
            try
            {
                combo.Items.Filter = null;
                RestoreEditor(editor, text, caret);
            }
            finally
            {
                combo.SetValue(SuppressedProperty, false);
            }
        }

        /// <summary>
        /// Filtering the selected item out of the list makes the ComboBox blank its editor.
        /// That text is the search the user is halfway through typing, so it goes back.
        /// </summary>
        private static void RestoreEditor(TextBox editor, string text, int caret)
        {
            if (editor == null || text == null || editor.Text == text)
                return;

            editor.Text = text;
            editor.CaretIndex = Math.Min(caret, text.Length);
        }

        /// <summary>Leaves the caret where the typing left it, with nothing selected.</summary>
        private static void CollapseSelection(TextBox editor, string text, int caret)
        {
            if (editor == null || editor.Text != text || editor.SelectionLength == 0)
                return;

            editor.Select(Math.Min(caret, text.Length), 0);
        }

        private static TextBox Editor(ComboBox combo)
        {
            combo.ApplyTemplate();
            return combo.Template == null ? null : combo.Template.FindName("PART_EditableTextBox", combo) as TextBox;
        }

        #endregion

        #region jira lookup

        /// <summary>
        /// Nothing in the list matches, so Jira gets asked — but only once the typing pauses,
        /// and only once per text, so holding a key down is still a single query.
        /// </summary>
        private static void ScheduleLookup(ComboBox combo, string text)
        {
            if (GetLookup(combo) == null || text == (string)combo.GetValue(LookedUpProperty))
                return;

            DispatcherTimer timer = (DispatcherTimer)combo.GetValue(LookupTimerProperty);
            if (timer == null)
            {
                timer = new DispatcherTimer(DispatcherPriority.Normal, combo.Dispatcher);
                timer.Interval = TimeSpan.FromMilliseconds(LookupDelayMs);
                timer.Tick += (sender, e) => RunLookup(combo);

                combo.SetValue(LookupTimerProperty, timer);
            }

            timer.Stop();
            timer.Tag = text;
            timer.Start();
        }

        private static void StopLookup(ComboBox combo)
        {
            DispatcherTimer timer = (DispatcherTimer)combo.GetValue(LookupTimerProperty);
            if (timer != null)
                timer.Stop();
        }

        private static void RunLookup(ComboBox combo)
        {
            DispatcherTimer timer = (DispatcherTimer)combo.GetValue(LookupTimerProperty);
            if (timer == null)
                return;

            timer.Stop();

            IssueLookup lookup = GetLookup(combo);
            string text = timer.Tag as string;

            if (lookup == null || string.IsNullOrEmpty(text))
                return;

            combo.SetValue(LookedUpProperty, text);

            lookup(text, () =>
            {
                // Whatever it found joined the list the combo is bound to; re-running the
                // search is what brings it on screen. Only if it is still the search on
                // screen, though — the answer may well arrive after the user moved on.
                TextBox editor = Editor(combo);
                if (editor == null || editor.Text != text || !combo.IsKeyboardFocusWithin)
                    return;

                ApplyFilter(combo, editor);
            });
        }

        #endregion
    }
}
