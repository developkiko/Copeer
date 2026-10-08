param([string]$Action)
Add-Type -Namespace W -Name K -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
'@
$script:EXT = 1
$script:UP = 2
function Tap([byte]$vk, [uint32]$flags) {
    [W.K]::keybd_event($vk, 0, $flags, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 45
}
switch ($Action) {
    'wt'  { Tap 0x5B $EXT; Tap 0x54 0; Tap 0x54 $UP; Tap 0x5B ($EXT -bor $UP) }
    'we'  { Tap 0x5B $EXT; Tap 0xBE 0; Tap 0xBE $UP; Tap 0x5B ($EXT -bor $UP) }
    'wss' { Tap 0x5B $EXT; Tap 0x10 0; Tap 0x53 0; Tap 0x53 $UP; Tap 0x10 $UP; Tap 0x5B ($EXT -bor $UP) }
    'wv'  { Tap 0x5B $EXT; Tap 0x56 0; Tap 0x56 $UP; Tap 0x5B ($EXT -bor $UP) }
    'esc' { Tap 0x1B 0; Tap 0x1B $UP }
}
Write-Output "sent: $Action"
