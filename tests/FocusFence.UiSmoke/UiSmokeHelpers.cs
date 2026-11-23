using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusFence.Core;

namespace FocusFence.UiSmoke;

internal static class UiSmokeHelpers
{
    public static Button? FindButton(DependencyObject root, string content)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button && button.Content?.ToString() == content) return button;
            if (FindButton(child, content) is { } found) return found;
        }
        return null;
    }

    public static void RenderTabs(TabControl pages, Window window, string basePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(basePath))!);
        for (var index = 0; index < pages.Items.Count; index++)
        {
            pages.SelectedIndex = index;
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = index == 0 ? basePath : Path.ChangeExtension(basePath, $"tab{index}.png");
            using (var output = File.Create(path)) encoder.Save(output);
        }
    }

    public static void VerifyManagedAccount(Window window, TabControl pages, FocusFence.App.AppAccessEditor appEditor, FocusFence.App.ManagedAccountEditor managedEditor, MemoryStore store, MemoryAccountControl accountControl)
    {
        store.State.ManagedDisabledApps.Add(new("Sample game", @"C:\Apps\Game.exe"));
        ((TextBox)managedEditor.FindName("AccountName")).Text = "TestStandardUser";
        ((CheckBox)managedEditor.FindName("RestrictNewApps")).IsChecked = true;
        ((Button)managedEditor.FindName("SaveDraftButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (store.State.ManagedDraft != new ManagedPolicyDraft("TestStandardUser", true, false) || accountControl.Request is not null)
            throw new Exception("Saving the draft did not persist options independently of administrator policy application.");
        ((Button)appEditor.FindName("ReviewDraftButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (pages.SelectedItem != window.FindName("ManagedAccountTab")) throw new Exception("App list review did not navigate to the managed-account tab.");
        var reopened = new FocusFence.App.ManagedAccountEditor();
        reopened.Initialize(accountControl, store.State, () => true, () => true);
        if (((TextBox)reopened.FindName("AccountName")).Text != "TestStandardUser" || ((CheckBox)reopened.FindName("RestrictNewApps")).IsChecked != true)
            throw new Exception("Saved account options were not restored in the editor.");
        var failed = new MemoryAccountControl();
        reopened.Initialize(failed, store.State, () => true, () => false);
        ((TextBox)reopened.FindName("AccountName")).Text = "UnsavedAccount";
        ((Button)reopened.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (store.State.ManagedDraft.AccountName != "TestStandardUser" || failed.Request is not null)
            throw new Exception("A failed draft save changed state or launched the administrator helper.");
        Console.WriteLine("PASS: Account draft persists, reopens, navigates from the app list, and blocks administrator launch on save failure.");
        ((Button)managedEditor.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (accountControl.Request?.AccountName != "TestStandardUser" || accountControl.Request.RestrictNewApps != true || accountControl.Request.BlockedExecutables.Single() != @"C:\Apps\Game.exe")
            throw new Exception("Managed account request was not passed to the administrator service.");
        var result = (TextBlock)managedEditor.FindName("Result");
        if (!result.Text.Contains("Local policy applied")) throw new Exception("Applied result not shown.");
        accountControl.NextOutcome = ManagedPolicyOutcome.Cancelled;
        ((Button)managedEditor.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!result.Text.Contains("Review cancelled")) throw new Exception("Cancelled review was not distinguished from apply.");
        ((Button)managedEditor.FindName("RestoreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (accountControl.RestoredAccount != "TestStandardUser") throw new Exception("Restore used the wrong account.");
        if (!result.Text.Contains("Previous policy restored")) throw new Exception("Restored result not shown.");
        accountControl.RestoreOutcome = ManagedPolicyOutcome.NoPolicyToRestore;
        ((Button)managedEditor.FindName("RestoreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!result.Text.Contains("No active FocusFence policy")) throw new Exception("No-policy result not shown.");
        accountControl.RestoreOutcome = ManagedPolicyOutcome.RestoredServiceRunning;
        ((Button)managedEditor.FindName("RestoreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!result.Text.Contains("kept Application Identity running")) throw new Exception("Service restore caveat not shown.");
        Console.WriteLine("PASS: Administrator outcomes distinguish apply, cancel, restore, no policy, and a still-running service.");
        accountControl.Fail = true; ((Button)managedEditor.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!((TextBlock)managedEditor.FindName("Result")).Text.Contains("Test failure")) throw new Exception("Managed account failure was not shown.");
        Console.WriteLine("PASS: Managed account controls forward account/options, restore, and report helper errors without elevation.");
    }

    public static void VerifyPinLogin(Application app, Window protectedWindow, string basePath)
    {
        _ = protectedWindow.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = app.Windows.OfType<FocusFence.App.PinDialog>().Single();
            dialog.UpdateLayout();
            var loginImage = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            loginImage.Render(dialog);
            var loginEncoder = new PngBitmapEncoder(); loginEncoder.Frames.Add(BitmapFrame.Create(loginImage));
            using (var output = File.Create(Path.ChangeExtension(basePath, "login.png"))) loginEncoder.Save(output);
            ((PasswordBox)dialog.FindName("Pin")).Password = "123456";
            ((Button)dialog.FindName("Submit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        ((Button)protectedWindow.FindName("SignInButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!((TabControl)protectedWindow.FindName("Pages")).IsEnabled) throw new Exception("PIN dialog did not unlock the UI.");
        protectedWindow.Close();
        if (((TabControl)protectedWindow.FindName("Pages")).IsEnabled) throw new Exception("Closing to tray did not lock controls.");
    }
}
