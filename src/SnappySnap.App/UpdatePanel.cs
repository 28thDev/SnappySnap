using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnappySnap.Application;
using SnappySnap.Core;
using SnappySnap.Infrastructure;
using SnappySnap.Presentation;
using SnappySnap.Localization;

namespace SnappySnap.App;

public sealed class UpdatePanel : StackPanel
{
    public UpdatePanel(UpdateSettings draft, UpdateCoordinator updates, Func<Task> install, Func<Task> checkNow)
    {
        Children.Add(Ui.Text("SnappySnap " + AppVersion.Display, 17));
        var automatic = new CheckBox { Content = Ui.Text("Check automatically"), IsChecked = draft.AutomaticChecks, Margin = new Thickness(0, 12, 0, 10) };
        automatic.Checked += (_, _) => draft.AutomaticChecks = true; automatic.Unchecked += (_, _) => draft.AutomaticChecks = false;
        Children.Add(automatic);
        Children.Add(Ui.Text("Official GitHub Releases", 12, "Muted"));
        var status = Ui.Text("", 13); status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 12, 0, 4);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); Children.Add(status);
        var last = Ui.Text("", 12, "Muted"); Children.Add(last);
        var details = Ui.Text("", 12, "Muted"); details.TextWrapping = TextWrapping.Wrap;
        Children.Add(new ScrollViewer { Content = details, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6, Margin = new Thickness(0, 10, 0, 10) }; Ui.Localize(progress, AutomationProperties.NameProperty, "Update download"); Children.Add(progress);
        var buttons = new WrapPanel();
        var check = Ui.Button("Check now", "\uE72C", async (_, _) => await checkNow());
        var download = Ui.Button("Download", "\uE896", async (_, _) => await updates.DownloadAsync());
        var cancel = Ui.Button("Cancel", "", (_, _) => updates.Cancel());
        var apply = Ui.Button("Update and restart", "\uE777", async (_, _) => await install(), "PrimaryButton");
        foreach (var b in new[] { check, download, cancel, apply }) buttons.Children.Add(b); Children.Add(buttons);
        void Refresh()
        {
            status.Text = updates.Message;
            last.Text = updates.LastCheck is null ? L.T("Not checked yet") : L.F("Last checked: {0:g}", updates.LastCheck.Value);
            details.Text = updates.Offer is null ? "" : L.F("Version {0} · {1}\n{2}", updates.Offer.Release.Version, FileSizeFormatter.Format(updates.Offer.Release.Size, L.Culture), updates.Offer.Release.Notes);
            progress.Value = updates.Progress; progress.Visibility = updates.State == UpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
            check.IsEnabled = !updates.Busy;
            download.Visibility = updates.Offer is not null && updates.Download is null ? Visibility.Visible : Visibility.Collapsed; download.IsEnabled = !updates.Busy;
            cancel.Visibility = updates.State is UpdateState.Checking or UpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
            apply.Visibility = updates.Download is not null ? Visibility.Visible : Visibility.Collapsed; apply.IsEnabled = !updates.Busy;
        }
        EventHandler changed = (_, _) => { if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(Refresh); };
        Loaded += (_, _) => { updates.Changed += changed; Refresh(); }; Unloaded += (_, _) => updates.Changed -= changed;
        Refresh();
    }
}
