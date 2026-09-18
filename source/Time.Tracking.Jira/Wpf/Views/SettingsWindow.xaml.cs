using Time.Tracking.Jira.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace Time.Tracking.Jira.Wpf.Views
{
    /// <summary>
    /// Same public contract the WinForms SettingsForm had — a settings reference — so
    /// LedgerViewModel.EditSettings() needed only its dialog call rewritten, not its logic.
    /// </summary>
    internal partial class SettingsWindow : Window
    {
        internal Settings settings { get; private set; }

        /// <summary>Non-empty only right after a legacy import — the caller adds these live.</summary>
        internal System.Collections.Generic.List<GridPersistedRow> ImportedRows { get { return vm.ImportedRows; } }

        private readonly SettingsViewModel vm;

        internal SettingsWindow(Settings settings)
        {
            this.settings = settings;

            InitializeComponent();

            vm = new SettingsViewModel();
            vm.Load(settings);
            DataContext = vm;

            // PasswordBox has no bindable Password property, so seed it by hand — and keep
            // seeding it: a legacy import can change ApiToken from code, which otherwise the
            // box would never pick up.
            TokenBox.Password = vm.ApiToken ?? "";
            vm.PropertyChanged += Vm_PropertyChanged;
        }

        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ApiToken" && TokenBox.Password != vm.ApiToken)
                TokenBox.Password = vm.ApiToken ?? "";
        }

        private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            vm.ApiToken = TokenBox.Password;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            vm.Save(settings);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }
    }
}
