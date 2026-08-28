param([int]$Tabs = 5, [string]$OutDir = "D:\tmp_shots")

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();' -Name Dpi -Namespace ShotUtil
[void][ShotUtil.Dpi]::SetProcessDPIAware()

$sig = @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@
Add-Type -MemberDefinition $sig -Name U32 -Namespace ShotUtil
$u = [ShotUtil.U32]

$proc = Get-Process -Name 'SecRandom.Sim.Avalonia' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Error "sim window not found"; exit 1 }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

[void]$u::ShowWindow($proc.MainWindowHandle, 9)   # SW_RESTORE
[void]$u::SetForegroundWindow($proc.MainWindowHandle)
Start-Sleep -Milliseconds 900

$r = [ShotUtil.U32+RECT]::new()
for ($i = 0; $i -lt $Tabs; $i++) {
    [void]$u::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Milliseconds 250
    [void]$u::GetWindowRect($proc.MainWindowHandle, [ref]$r)
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $path = Join-Path $OutDir "tab$i.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output "captured $path ($w x $h)"
    [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')
    Start-Sleep -Milliseconds 700
}
