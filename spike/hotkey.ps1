<#
.SYNOPSIS
Phase 0: hotkey to save a clip without leaving the game.

.DESCRIPTION
Registers the key with RegisterHotKey (not a global keyboard hook: Windows only
notifies when the registered combination is pressed) and runs save.ps1. Keep it
running in a separate window while record.ps1 records. Ctrl+C exits.

.EXAMPLE
.\spike\hotkey.ps1
.\spike\hotkey.ps1 -Hotkey "Ctrl+Shift+F9" -Game "Cyberpunk 2077"
#>
[CmdletBinding()]
param(
    [string]$Hotkey = 'Alt+F10',
    [string]$Game = 'Desktop',
    [int]$ClipSeconds = 60
)
. "$PSScriptRoot\common.ps1"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class GhostclipHotkey
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint ms, uint wakeMask);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

    private const uint MOD_NOREPEAT = 0x4000;
    private const uint WM_HOTKEY = 0x0312;
    private const uint QS_ALLINPUT = 0x04FF;
    private const uint PM_REMOVE = 1;

    public static void Register(uint modifiers, uint vk)
    {
        if (!RegisterHotKey(IntPtr.Zero, 1, modifiers | MOD_NOREPEAT, vk))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void Unregister() { UnregisterHotKey(IntPtr.Zero, 1); }

    // Waits up to ms milliseconds and says whether the hotkey was pressed. The
    // loop stays in PowerShell so Ctrl+C works between calls.
    public static bool Wait(int ms)
    {
        MsgWaitForMultipleObjects(0, null, false, (uint)ms, QS_ALLINPUT);
        bool hit = false;
        MSG msg;
        while (PeekMessageW(out msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            if (msg.message == WM_HOTKEY) hit = true;
        return hit;
    }
}
'@

$modifiers = 0
$key = $null
foreach ($part in $Hotkey -split '\+') {
    switch ($part.Trim().ToLowerInvariant()) {
        'alt' { $modifiers = $modifiers -bor 1 }
        'ctrl' { $modifiers = $modifiers -bor 2 }
        'shift' { $modifiers = $modifiers -bor 4 }
        'win' { $modifiers = $modifiers -bor 8 }
        default { $key = [Windows.Forms.Keys]$part.Trim() }
    }
}
if ($null -eq $key) { throw "Hotkey has no main key: $Hotkey" }

try {
    [GhostclipHotkey]::Register([uint32]$modifiers, [uint32]$key)
}
catch {
    throw "Could not register $Hotkey (another program may be using it): $($_.Exception.InnerException.Message)"
}
Write-Host "Hotkey $Hotkey active. Ctrl+C exits."
try {
    while ($true) {
        if ([GhostclipHotkey]::Wait(250)) {
            try {
                & "$PSScriptRoot\save.ps1" -Game $Game -ClipSeconds $ClipSeconds | Out-Null
            }
            catch {
                Write-Warning $_
            }
        }
    }
}
finally {
    [GhostclipHotkey]::Unregister()
}
