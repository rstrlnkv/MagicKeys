# MagicKeys — настройка Apple Magic Keyboard для Windows.
# Copyright (C) 2026 r.strlnkv
# Свободная программа под GNU General Public License v3 или новее; см. LICENSE.
#
# Переводит LICENSE в RTF: окно с текстом лицензии в установщике умеет только его.
param([string]$Source, [string]$Target)

$text = [System.IO.File]::ReadAllText($Source)
$text = $text -replace '\\', '\\\\' -replace '\{', '\{' -replace '\}', '\}'
$text = $text -replace "`r`n", "`n"

$body = New-Object System.Text.StringBuilder
foreach ($line in $text -split "`n") {
    [void]$body.Append($line)
    [void]$body.Append("\par`r`n")
}

$rtf = "{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil\fcharset0 Consolas;}}`r`n\fs16 " +
       $body.ToString() + "}"
[System.IO.File]::WriteAllText($Target, $rtf, [System.Text.Encoding]::ASCII)
"Лицензия для установщика: $Target"
