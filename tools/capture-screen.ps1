<#
.SYNOPSIS
    Captures the Windows screen to a PNG inside this project, so it can be opened and read later.

.DESCRIPTION
    Uses only built-in Windows PowerShell and .NET (System.Drawing / System.Windows.Forms).
    By default it captures every monitor (the whole virtual screen) at full resolution and
    overwrites .screenshots\latest.png. The .screenshots folder is git-ignored.

.PARAMETER Primary
    Capture only the primary monitor.

.PARAMETER Path
    Where to save the PNG. Default: <repo>\.screenshots\latest.png

.PARAMETER DelaySeconds
    Wait this long before capturing, to give you time to switch to the window you want.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture-screen.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture-screen.ps1 -Primary -DelaySeconds 3
#>
[CmdletBinding()]
param(
    [switch]$Primary,
    [string]$Path = (Join-Path (Split-Path -Parent $PSScriptRoot) '.screenshots\latest.png'),
    [int]$DelaySeconds = 0
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Without this, a display scaled above 100% is captured at its scaled (smaller) size, and blurry.
Add-Type -Namespace Native -Name Dpi -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[void][Native.Dpi]::SetProcessDPIAware()

if ($DelaySeconds -gt 0) {
    Start-Sleep -Seconds $DelaySeconds
}

$bounds = if ($Primary) { [System.Windows.Forms.Screen]::PrimaryScreen.Bounds } else { [System.Windows.Forms.SystemInformation]::VirtualScreen }

$folder = Split-Path -Parent $Path
if (-not (Test-Path -LiteralPath $folder)) {
    [void](New-Item -ItemType Directory -Path $folder)
}

$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
try {
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    }
    finally {
        $graphics.Dispose()
    }

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $bitmap.Dispose()
}

Write-Output "Saved $($bounds.Width)x$($bounds.Height) screenshot to $Path"
