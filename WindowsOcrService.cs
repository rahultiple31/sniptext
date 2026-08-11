using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SnipText
{
    public static class WindowsOcrService
    {
        public static async Task<string> RecognizeAsync(BitmapSource image)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "SnipText");
            Directory.CreateDirectory(tempDirectory);

            var imagePaths = new List<string>();
            var scriptPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".ps1");

            try
            {
                foreach (var rowImage in OcrImagePreprocessor.CreateRowImages(image))
                {
                    var imagePath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".png");
                    ImageFile.SavePng(rowImage, imagePath);
                    imagePaths.Add(imagePath);
                }

                File.WriteAllText(scriptPath, BuildOcrScript(imagePaths), Encoding.UTF8);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(startInfo))
                {
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());

                    var output = await outputTask;
                    var error = await errorTask;

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Windows OCR returned an error." : error.Trim());
                    }

                    return output.TrimEnd('\r', '\n');
                }
            }
            finally
            {
                foreach (var imagePath in imagePaths)
                {
                    TryDelete(imagePath);
                }

                TryDelete(scriptPath);
            }
        }

        private static string BuildOcrScript(IReadOnlyList<string> imagePaths)
        {
            var pathList = string.Join(", ", imagePaths.Select(path => "'" + path.Replace("'", "''") + "'"));

            return @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Runtime.WindowsRuntime
[Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime] > $null
[Windows.Storage.FileAccessMode, Windows.Storage, ContentType=WindowsRuntime] > $null
[Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime] > $null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime] > $null
[Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType=WindowsRuntime] > $null
[Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime] > $null
[Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType=WindowsRuntime] > $null

function Await-WinRt($operation, [Type]$resultType) {
    $method = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Length -eq 1 } |
        Select-Object -First 1

    $task = $method.MakeGenericMethod($resultType).Invoke($null, @($operation))
    return $task.Result
}

function Repair-OcrText($text) {
    $clean = $text.Replace([string][char]0x00D8, '0').Replace([string][char]0x00F8, '0')
    $clean = $clean -replace '([A-Za-z0-9_%+\-])\s+\.\s+([A-Za-z]{2,})', '$1.$2'

    $tokens = $clean -split '(\s+)'
    for ($i = 0; $i -lt $tokens.Length; $i++) {
        $token = $tokens[$i]
        if ($token.Length -ge 10 -and $token -match '[A-Za-z]' -and $token -match '\d') {
            $tokens[$i] = $token.Replace(')', 'J').Replace('}', 'J').Replace(']', 'J')
        }
    }

    return [string]::Concat($tokens)
}

function New-OcrWordItem($word) {
    $bounds = $word.BoundingRect

    return [PSCustomObject]@{
        Text = [string]$word.Text
        X = [double]$bounds.X
        Y = [double]$bounds.Y
        Width = [double]$bounds.Width
        Height = [double]$bounds.Height
        CenterY = [double]($bounds.Y + ($bounds.Height / 2.0))
    }
}

function Join-OrderedWords($words) {
    $orderedWords = @($words | Sort-Object @{ Expression = { $_.X } }, @{ Expression = { $_.Y } })
    return [string]::Join(' ', @($orderedWords | ForEach-Object { $_.Text }))
}

function Get-OrderedTextRows($result) {
    $words = New-Object System.Collections.Generic.List[object]

    foreach ($line in $result.Lines) {
        foreach ($word in $line.Words) {
            if (-not [string]::IsNullOrWhiteSpace($word.Text)) {
                $words.Add((New-OcrWordItem $word)) > $null
            }
        }
    }

    if ($words.Count -eq 0) {
        return @()
    }

    $rows = New-Object System.Collections.Generic.List[object]
    $orderedWords = @($words | Sort-Object @{ Expression = { $_.CenterY } }, @{ Expression = { $_.X } })

    foreach ($word in $orderedWords) {
        $bestRow = $null
        $bestDistance = [double]::MaxValue

        foreach ($row in $rows) {
            $distance = [Math]::Abs($word.CenterY - $row.CenterY)
            $limit = [Math]::Max(10.0, [Math]::Max($row.Height, $word.Height) * 0.70)

            if ($distance -le $limit -and $distance -lt $bestDistance) {
                $bestRow = $row
                $bestDistance = $distance
            }
        }

        if ($null -eq $bestRow) {
            $rowWords = New-Object System.Collections.Generic.List[object]
            $rowWords.Add($word) > $null

            $rows.Add([PSCustomObject]@{
                Words = $rowWords
                CenterY = $word.CenterY
                Height = $word.Height
                Count = 1
            }) > $null
        }
        else {
            $bestRow.Words.Add($word) > $null
            $bestRow.CenterY = (($bestRow.CenterY * $bestRow.Count) + $word.CenterY) / ($bestRow.Count + 1)
            $bestRow.Height = [Math]::Max($bestRow.Height, $word.Height)
            $bestRow.Count = $bestRow.Count + 1
        }
    }

    return @($rows |
        Sort-Object @{ Expression = { $_.CenterY } } |
        ForEach-Object {
            $rowText = Join-OrderedWords $_.Words
            if (-not [string]::IsNullOrWhiteSpace($rowText)) {
                Repair-OcrText $rowText
            }
        })
}

function Get-FallbackLineRows($result) {
    $fallbackRows = New-Object System.Collections.Generic.List[string]

    foreach ($line in $result.Lines) {
        $rowText = $line.Text

        if ([string]::IsNullOrWhiteSpace($rowText)) {
            $words = New-Object System.Collections.Generic.List[string]

            foreach ($word in $line.Words) {
                if (-not [string]::IsNullOrWhiteSpace($word.Text)) {
                    $words.Add($word.Text) > $null
                }
            }

            $rowText = [string]::Join(' ', $words)
        }

        if (-not [string]::IsNullOrWhiteSpace($rowText)) {
            $fallbackRows.Add((Repair-OcrText $rowText)) > $null
        }
    }

    return @($fallbackRows)
}

$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()

if ($null -eq $engine) {
    throw 'Windows OCR is not available for the current user language.'
}

$rows = New-Object System.Collections.Generic.List[string]
$imagePaths = @(" + pathList + @")

foreach ($imagePath in $imagePaths) {
    $file = Await-WinRt ([Windows.Storage.StorageFile]::GetFileFromPathAsync($imagePath)) ([Windows.Storage.StorageFile])
    $stream = Await-WinRt ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
    $decoder = Await-WinRt ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $softwareBitmap = Await-WinRt ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $result = Await-WinRt ($engine.RecognizeAsync($softwareBitmap)) ([Windows.Media.Ocr.OcrResult])

    $orderedRows = @(Get-OrderedTextRows $result)

    if ($orderedRows.Count -eq 0) {
        $orderedRows = @(Get-FallbackLineRows $result)
    }

    foreach ($row in $orderedRows) {
        if (-not [string]::IsNullOrWhiteSpace($row)) {
            $rows.Add($row) > $null
        }
    }
}

Write-Output ([string]::Join([Environment]::NewLine, $rows))
";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
