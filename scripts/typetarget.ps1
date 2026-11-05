#Requires -Version 5.1
<#
.SYNOPSIS
    A throwaway text target for the self-test.

.DESCRIPTION
    Opens one WinForms window whose title mirrors the text in its TextBox
    ("CU-TYPE-TARGET|<text>"). The self-test types into it and then reads the
    window title back, which proves the injected text survived exactly - quotes,
    backticks, dollar signs, ampersands and CJK included - without touching any
    real application's document.

    Kill the process when done: the window is not meant to be closed by hand.
#>
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = New-Object System.Windows.Forms.Form
$form.Text = 'CU-TYPE-TARGET'
$form.Width = 900
$form.Height = 200
$form.StartPosition = 'CenterScreen'

$box = New-Object System.Windows.Forms.TextBox
$box.Multiline = $true
$box.Dock = 'Fill'
$box.Font = New-Object System.Drawing.Font('Consolas', 14)
$form.Controls.Add($box)

$box.add_TextChanged({
    $form.Text = 'CU-TYPE-TARGET|' + $box.Text
})

$form.Add_Shown({ $box.Focus() })
[System.Windows.Forms.Application]::Run($form)
