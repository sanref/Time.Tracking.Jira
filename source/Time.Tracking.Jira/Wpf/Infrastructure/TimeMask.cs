using Time.Tracking.Jira.Logging;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Time.Tracking.Jira.Wpf.Infrastructure
{
    /// <summary>
    /// Holds a <see cref="TextBox"/> to a 24-hour <c>HH:mm</c> value: digits only, the colon
    /// appears on its own after the hour, each position is range-checked as it is typed, and a
    /// valid value is padded to <c>HH:mm</c> when the field loses focus.
    /// </summary>
    public static class TimeMask
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(TimeMask), new PropertyMetadata(false, OnEnabledChanged));

        // Set while the mask writes Text itself, so the TextChanged it causes is skipped.
        private static readonly DependencyProperty EchoProperty = DependencyProperty.RegisterAttached(
            "Echo", typeof(bool), typeof(TimeMask), new PropertyMetadata(false));

        public static void SetEnabled(DependencyObject element, bool value)
        {
            element.SetValue(EnabledProperty, value);
        }

        public static bool GetEnabled(DependencyObject element)
        {
            return (bool)element.GetValue(EnabledProperty);
        }

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            TextBox box = d as TextBox;
            if (box == null)
                return;

            // This runs while the XAML parser builds the window, so anything thrown here
            // surfaces as "Set property 'TimeMask.Enabled' threw an exception" and takes the
            // whole dialog — and with it the application — down. A field that is merely not
            // masked is a far better outcome: MaxLength still bounds it and the view model
            // still rejects anything that is not HH:mm.
            try
            {
                if ((bool)e.NewValue)
                {
                    box.PreviewTextInput += OnPreviewTextInput;
                    box.PreviewKeyDown += OnPreviewKeyDown;
                    box.TextChanged += OnTextChanged;
                    box.LostFocus += OnLostFocus;
                    DataObject.AddPastingHandler(box, OnPaste);
                }
                else
                {
                    box.PreviewTextInput -= OnPreviewTextInput;
                    box.PreviewKeyDown -= OnPreviewKeyDown;
                    box.TextChanged -= OnTextChanged;
                    box.LostFocus -= OnLostFocus;
                    DataObject.RemovePastingHandler(box, OnPaste);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log("TimeMask could not be attached; the field stays unmasked.", ex);
            }
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // A space would otherwise land in the field (and the window's shortcuts swallow it
            // inconsistently anyway).
            if (e.Key == Key.Space)
                e.Handled = true;
        }

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            TextBox box = (TextBox)sender;

            string candidate = box.Text.Substring(0, box.SelectionStart)
                             + e.Text
                             + box.Text.Substring(box.SelectionStart + box.SelectionLength);

            e.Handled = !Acceptable(Digits(candidate));
        }

        private static void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            e.CancelCommand();

            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
                return;

            string digits = Digits((string)e.DataObject.GetData(DataFormats.UnicodeText));
            if (digits.Length == 0)
                return;

            string clamped = Clamp(digits);
            Write((TextBox)sender, Format(clamped), clamped.Length > 2 ? clamped.Length + 1 : clamped.Length);
        }

        private static void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox box = (TextBox)sender;
            if ((bool)box.GetValue(EchoProperty))
                return;

            int caret = Math.Min(box.CaretIndex, box.Text.Length);
            int digitsBeforeCaret = Digits(box.Text.Substring(0, caret)).Length;

            string clamped = Clamp(Digits(box.Text));
            string formatted = Format(clamped);
            if (formatted == box.Text)
                return;

            Write(box, formatted, digitsBeforeCaret + (digitsBeforeCaret > 2 ? 1 : 0));
        }

        private static void OnLostFocus(object sender, RoutedEventArgs e)
        {
            TextBox box = (TextBox)sender;

            string digits = Digits(box.Text);
            if (digits.Length == 0)
                return;

            // "9" -> "09:00", "9:5" -> "09:05"
            while (digits.Length < 4)
                digits = digits.Length < 2 ? "0" + digits : digits + "0";

            string padded = digits.Substring(0, 2) + ":" + digits.Substring(2, 2);
            if (padded != box.Text)
                Write(box, padded, padded.Length);

            // The Text binding updates on LostFocus too; push explicitly so it never matters
            // which handler ran first.
            BindingExpression binding = box.GetBindingExpression(TextBox.TextProperty);
            if (binding != null)
                binding.UpdateSource();
        }

        private static void Write(TextBox box, string text, int caret)
        {
            box.SetValue(EchoProperty, true);
            try
            {
                box.Text = text;
                box.CaretIndex = Math.Min(Math.Max(caret, 0), text.Length);
            }
            finally
            {
                box.SetValue(EchoProperty, false);
            }
        }

        private static string Digits(string value)
        {
            return new string((value ?? "").Where(char.IsDigit).ToArray());
        }

        /// <summary>Trims to four digits and drops the trailing ones that break the HH:mm ranges.</summary>
        private static string Clamp(string digits)
        {
            for (int length = Math.Min(digits.Length, 4); length >= 0; length--)
                if (Acceptable(digits.Substring(0, length)))
                    return digits.Substring(0, length);

            return "";
        }

        /// <summary>Whether these leading digits can still grow into a valid 24-hour HH:mm.</summary>
        private static bool Acceptable(string digits)
        {
            if (digits.Length > 4)
                return false;
            if (digits.Length >= 1 && digits[0] > '2')
                return false;
            if (digits.Length >= 2 && (digits[0] - '0') * 10 + (digits[1] - '0') > 23)
                return false;
            if (digits.Length >= 3 && digits[2] > '5')
                return false;

            return true;
        }

        private static string Format(string digits)
        {
            return digits.Length <= 2 ? digits : digits.Substring(0, 2) + ":" + digits.Substring(2);
        }
    }
}
