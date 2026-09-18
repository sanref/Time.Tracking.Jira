using System.Diagnostics;
using System.Windows;

namespace Time.Tracking.Jira.Wpf.Views
{
    public partial class AboutWindow : Window
    {
        private const string LicenseUrl = "https://www.apache.org/licenses/LICENSE-2.0";
        private const string ContactMail = "sanref@gmail.com";

        public AboutWindow()
        {
            InitializeComponent();

            // AppInfo.ShortVersion, not Assembly.GetName().Version: AssemblyVersion is
            // pinned per major release (see GitVersion.yml) and would always read "4.0".
            VersionText.Text = "v" + AppInfo.ShortVersion;
        }

        private void Mail_Click(object sender, RoutedEventArgs e)
        {
            Open("mailto:" + ContactMail);
        }

        private void License_Click(object sender, RoutedEventArgs e)
        {
            Open(LicenseUrl);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch { /* nothing useful to show the user here */ }
        }
    }
}
