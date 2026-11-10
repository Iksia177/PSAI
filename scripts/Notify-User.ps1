param(
    [string] $Message = 'PSAI 桌面提醒测试。以后需要你介入时，会保存进度、暂停目标，并弹出此类提醒。',
    [string] $Title = 'PSAI 需要你操作'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[System.Media.SystemSounds]::Exclamation.Play()
[void] [System.Windows.Forms.MessageBox]::Show(
    $Message,
    $Title,
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information,
    [System.Windows.Forms.MessageBoxDefaultButton]::Button1,
    [System.Windows.Forms.MessageBoxOptions]::DefaultDesktopOnly
)
