using System;
using System.Threading.Tasks;
using System.Windows;
using FocusFence.Core;

namespace FocusFence.App;

public partial class PinDialog : Window
{
    private readonly PinAuthentication authentication;
    private readonly bool configure;
    private bool working;
    public PinDialog(PinAuthentication authentication, bool configure)
    {
        InitializeComponent(); this.authentication = authentication; this.configure = configure;
        Heading.Text = configure ? "Set your FocusFence PIN" : "Unlock FocusFence";
        Hint.Text = configure ? "Use 6–12 digits. This PIN protects this app's controls; Windows administrator actions still require Windows credentials." : "Enter your FocusFence PIN. Blocking continues while the controls are locked.";
        ConfirmPin.Visibility = configure ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Pin.Focus();
        Closing += (_, e) => { if (working) e.Cancel = true; };
    }
    private async void SubmitPin(object sender, RoutedEventArgs e)
    {
        if (working) return;
        var value = Pin.Password;
        if (configure && value != ConfirmPin.Password) { Error.Text = "PINs do not match."; return; }
        if (!PinAuthentication.ValidFormat(value)) { Error.Text = "Use 6–12 digits."; return; }
        working = true; Submit.IsEnabled = false; Pin.Clear(); ConfirmPin.Clear();
        try
        {
            await Task.Yield();
            if (configure) authentication.SetPin(value);
            else
            {
                var result = authentication.Unlock(value);
                if (!result.Success) { Error.Text = result.Message; return; }
            }
            working = false; DialogResult = true;
        }
        catch (Exception ex) { Error.Text = "Could not update PIN authentication: " + ex.Message; }
        finally { working = false; Submit.IsEnabled = true; }
    }
}
