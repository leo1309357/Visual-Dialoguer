param(
    [string]$ThemeName = 'Light',
    [string]$CdlgPath = '',
    [int]$ClickX = -1,
    [int]$ClickY = -1,
    [int]$HoverX = -1,
    [int]$HoverY = -1,
    [int]$TabCount = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# NOTE: keep this file pure ASCII. Windows PowerShell 5.1 reads BOM-less .ps1
# as ANSI, and UTF-8 Chinese comments corrupt string terminators at parse time.

$exe = 'C:\personal\VisualDialoguer\src\VisualDialoguer.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\VisualDialoguer.exe'
$out = "C:\personal\VisualDialoguer\theme_$($ThemeName.ToLower()).png"

Get-Process -Name 'VisualDialoguer' -ErrorAction SilentlyContinue |
    ForEach-Object { try { $_.Kill() } catch {} }
Start-Sleep -Seconds 1

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public class W {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
}
'@

# Without DPI awareness GetWindowRect reports logical units (1280x820 at 150%
# shows up as 853x547) and the capture would crop the right/bottom of the UI.
[W]::SetProcessDPIAware() | Out-Null

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
if ($CdlgPath -ne '') { $psi.Arguments = "`"$CdlgPath`"" }
# Child processes inherit the parent environment; setting the variable here is
# more reliable than poking ProcessStartInfo.EnvironmentVariables from PS 5.1.
$env:VD_THEME = $ThemeName
$p = [System.Diagnostics.Process]::Start($psi)

Start-Sleep -Seconds 7
$p.Refresh()

if ($p.MainWindowHandle -eq [IntPtr]::Zero) {
    Write-Output 'NO_WINDOW'
    try { $p.Kill() } catch {}
    exit 1
}

[W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Seconds 2

$rect = New-Object RECT
[W]::GetWindowRect($p.MainWindowHandle, [ref]$rect) | Out-Null
$w = [int]($rect.Right - $rect.Left)
$h = [int]($rect.Bottom - $rect.Top)
if ($w -le 0 -or $h -le 0) {
    Write-Output 'NO_RECT'
    try { $p.Kill() } catch {}
    exit 1
}

# Optional click (window-relative) so a screenshot can show the selected /
# hover state of a list row instead of the empty document state.
if ($ClickX -ge 0 -and $ClickY -ge 0) {
    [W]::SetCursorPos($rect.Left + $ClickX, $rect.Top + $ClickY) | Out-Null
    Start-Sleep -Milliseconds 400
    [W]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [W]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 1200
} elseif ($HoverX -ge 0 -and $HoverY -ge 0) {
    [W]::SetCursorPos($rect.Left + $HoverX, $rect.Top + $HoverY) | Out-Null
    # SetCursorPos alone does not raise a pointer-move WinUI reacts to; a tiny
    # relative move does.
    [W]::mouse_event(0x0001, 2, 2, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    [W]::mouse_event(0x0001, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 900
}

# Keyboard focus ring: Tab moves focus without a pointer, which is the only
# way WinUI draws its focus visual.
for ($i = 0; $i -lt $TabCount; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
    Start-Sleep -Milliseconds 250
}
if ($TabCount -gt 0) { Start-Sleep -Milliseconds 600 }

# Prefer a real screen copy when the whole window is on screen: it captures the
# DWM-drawn caption bar, which PrintWindow cannot paint. Fall back to
# PrintWindow only for windows that extend past the desktop.
$screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
$onScreen = ($rect.Left -ge $screen.Left) -and ($rect.Top -ge $screen.Top) -and
            ($rect.Right -le ($screen.Left + $screen.Width)) -and
            ($rect.Bottom -le ($screen.Top + $screen.Height))

if ($onScreen) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
    $g.Dispose()
} else {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [W]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    Write-Output 'PRINTWINDOW'
}

$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$finalW = $bmp.Width
$finalH = $bmp.Height
$bmp.Dispose()

try { $p.Kill() } catch {}
Write-Output "SAVED $out ${finalW}x${finalH}"
