using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FocusFence.Core;

namespace FocusFence.App;

public partial class ManagedAccountEditor : UserControl
{
    private IManagedAccountControl? control;
    private AppState? state;
    private Func<bool> authorize = () => true;
    private Func<bool> save = () => false;
    public event EventHandler? DraftChanged;
    public ManagedAccountEditor() { InitializeComponent(); }
    public void Initialize(IManagedAccountControl? accountControl, AppState? appState = null, Func<bool>? authorizeAccess = null, Func<bool>? saveState = null)
    {
        control = accountControl;
        state = appState; authorize = authorizeAccess ?? (() => true);
        save = saveState ?? (() => false);
        if (state is not null)
        {
            AccountName.Text = state.ManagedDraft.AccountName;
            RestrictNewApps.IsChecked = state.ManagedDraft.RestrictNewApps;
            BlockUninstallers.IsChecked = state.ManagedDraft.BlockUninstallers;
        }
        ReviewButton.IsEnabled = RestoreButton.IsEnabled = control is not null;
    }
    private async void Review(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        var request = new ManagedAccountRequest(AccountName.Text.Trim(), RestrictNewApps.IsChecked == true, BlockUninstallers.IsChecked == true)
        { BlockedExecutables = state?.ManagedDisabledApps.ConvertAll(a => a.Path) ?? [] };
        request.Validate();
        PersistDraft();
        return await control!.ReviewAndApply(request);
    });
    private void SaveDraft(object sender, RoutedEventArgs e)
    {
        if (!authorize()) return;
        try { PersistDraft(); Result.Text = "Draft saved. Windows policy has not changed. Review and apply when ready."; }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
    private void PersistDraft()
    {
        if (state is null) throw new InvalidOperationException("Draft storage is unavailable.");
        var draft = new ManagedPolicyDraft(AccountName.Text.Trim(), RestrictNewApps.IsChecked == true, BlockUninstallers.IsChecked == true);
        new ManagedAccountRequest(draft.AccountName, true, false).Validate();
        var previous = state.ManagedDraft;
        state.ManagedDraft = draft;
        try
        {
            if (!save()) throw new System.IO.IOException("Could not save the draft. No administrator operation was started.");
        }
        catch { state.ManagedDraft = previous; throw; }
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }
    private async void Restore(object sender, RoutedEventArgs e) => await Execute(() => control!.Restore(AccountName.Text.Trim()));
    private async Task Execute(Func<Task<ManagedPolicyOutcome>> action)
    {
        if (control is null || !authorize()) return;
        ReviewButton.IsEnabled = RestoreButton.IsEnabled = SaveDraftButton.IsEnabled = false;
        AccountName.IsEnabled = RestrictNewApps.IsEnabled = BlockUninstallers.IsEnabled = false;
        var account = AccountName.Text.Trim();
        try
        {
            Result.Text = "Waiting for the administrator helper. Windows may request credentials.";
            var outcome = await action();
            var description = outcome switch
            {
                ManagedPolicyOutcome.Applied => "Local policy applied. Sign into the managed account to test enforcement. The saved draft is not a live policy monitor.",
                ManagedPolicyOutcome.Restored => "Previous policy restored. Your draft has been kept for future use.",
                ManagedPolicyOutcome.Cancelled => "Review cancelled. Windows policy was not changed by this request; the draft is still saved.",
                ManagedPolicyOutcome.NoPolicyToRestore => "No active FocusFence policy was found to restore.",
                ManagedPolicyOutcome.RestoredServiceRunning => "Previous policy and service startup mode restored. Windows kept Application Identity running; it may remain running until restart.",
                _ => throw new InvalidOperationException("Unrecognized administrator result. Verify the Windows policy before retrying.")
            };
            Result.Text = $"{account} · {DateTimeOffset.Now:t}\n{description}";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { Result.Text = "Administrator request cancelled. No changes were made by this request."; }
        catch (Exception ex) { Result.Text = ex.Message; }
        finally
        {
            ReviewButton.IsEnabled = RestoreButton.IsEnabled = SaveDraftButton.IsEnabled = true;
            AccountName.IsEnabled = RestrictNewApps.IsEnabled = BlockUninstallers.IsEnabled = true;
        }
    }
}
