# Brand assets

This folder holds the repository brand assets for Jeninnet.CQRS.

| File | Purpose |
|---|---|
| `icon.svg` | Vector master for the CQRS flow mark. |
| `icon.png` | 512x512 PNG used by the README and documentation. |
| `icon-128.png` | 128x128 PNG packed as `icon.png` into every NuGet package. |
| `generate-icon.ps1` | Regenerates both PNG sizes from the same drawing. Run it with Windows PowerShell (`powershell.exe -File assets/generate-icon.ps1`) so `System.Drawing` is available. |

Do not hand-edit the PNG files; regenerate them from the script so the sizes stay consistent.
