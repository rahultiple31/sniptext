# SnipText

A small Windows desktop app for cropping a screen region or opening an image, then converting the image into editable row-by-row text with Windows OCR. The app detects image rows before OCR and applies conservative scan cleanup for common OCR substitutions such as `Ø` in place of `0`.

## Run

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '..').Path
dotnet run
```

## Use

1. Select **New snip** and drag over the part of the screen you want to capture.
2. The capture appears in the preview panel.
3. OCR runs automatically when **Auto read** is enabled, or you can select **Read text**.
4. Each detected image row is written as a separate text row.
5. Copy or save the extracted text.

OCR uses the built-in Windows Runtime OCR engine, so no cloud service or API key is required.
