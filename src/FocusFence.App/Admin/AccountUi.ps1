Add-Type -AssemblyName System.Windows.Forms

function Show-Result([string]$message) {
    [void][System.Windows.Forms.MessageBox]::Show($message, 'FocusFence administrator controls', 'OK', 'Information')
}

function Confirm-Preview([string]$message) {
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Review FocusFence policy changes'
    $form.Size = New-Object System.Drawing.Size(820, 650)
    $form.StartPosition = 'CenterScreen'
    $text = New-Object System.Windows.Forms.TextBox
    $text.Multiline = $true; $text.ReadOnly = $true; $text.ScrollBars = 'Both'; $text.Dock = 'Fill'; $text.Text = $message
    $buttons = New-Object System.Windows.Forms.FlowLayoutPanel
    $buttons.Dock = 'Bottom'; $buttons.Height = 48
    $apply = New-Object System.Windows.Forms.Button
    $apply.Text = 'Apply'; $apply.Width = 120; $apply.DialogResult = 'OK'
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Cancel'; $cancel.Width = 120; $cancel.DialogResult = 'Cancel'
    [void]$buttons.Controls.Add($apply); [void]$buttons.Controls.Add($cancel)
    [void]$form.Controls.Add($text); [void]$form.Controls.Add($buttons)
    $form.CancelButton = $cancel
    try { return $form.ShowDialog() -eq 'OK' } finally { $form.Dispose() }
}
