# Скачивает Twemoji-картинки (72x72) для всех эмодзи из Core/EmojiData.cs в папку emoji/.
# Имя файла — hex-кодпоинты через '-', как в наборе Twemoji; FE0F (emoji presentation) выбрасывается.
# Символы, которых нет в Twemoji (тире, кавычки, стрелки), просто не скачаются — панель рисует их шрифтом.
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'emoji'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$src = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Core\EmojiData.cs')
$rx = [regex]'new\("([^"]+)"'

$codes = New-Object System.Collections.Generic.List[string]
foreach ($m in $rx.Matches($src)) {
    $s = $m.Groups[1].Value
    $parts = New-Object System.Collections.Generic.List[string]
    $i = 0
    while ($i -lt $s.Length) {
        $hi = [int][char]$s[$i]
        if ($hi -ge 0xD800 -and $hi -le 0xDBFF -and ($i + 1) -lt $s.Length) {
            $lo = [int][char]$s[$i + 1]
            $cp = 0x10000 + (($hi - 0xD800) -shl 10) + ($lo - 0xDC00)
            $i += 2
        } else { $cp = $hi; $i += 1 }
        if ($cp -ne 0xFE0F) { $parts.Add(('{0:x}' -f $cp)) }
    }
    if ($parts.Count -gt 0) { $codes.Add(($parts -join '-')) }
}
$codes = $codes | Sort-Object -Unique

$ok = 0; $miss = 0
foreach ($code in $codes) {
    $dest = Join-Path $out ($code + '.png')
    if (Test-Path $dest) { $ok++; continue }
    # twemoji хранит имена без ведущих нулей, но пробуем и дополненный вариант
    $padded = if ($code.Length -lt 4 -and $code -notmatch '-') { $code.PadLeft(4, '0') } else { $null }
    $urls = @("https://cdn.jsdelivr.net/gh/jdecked/twemoji@15.1.0/assets/72x72/$code.png")
    if ($padded) { $urls += "https://cdn.jsdelivr.net/gh/jdecked/twemoji@15.1.0/assets/72x72/$padded.png" }
    $done = $false
    foreach ($url in $urls) {
        curl.exe -sf -o $dest $url 2>$null
        if ($LASTEXITCODE -eq 0 -and (Test-Path $dest)) { $ok++; $done = $true; break }
    }
    if (-not $done) { $miss++ }
}
Write-Host "emoji: ok=$ok miss=$miss"
