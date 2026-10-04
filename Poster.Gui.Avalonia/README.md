# Poster.Gui.Avalonia

Avalonia front end for Poster, styled after Samsung One UI. Minimal layout (title, four page pills, cards),
One UI shapes and colours on every control.

    dotnet run --project Poster.Gui.Avalonia

## Where the look lives

- `Themes/OneUi/Tokens.axaml`   colours (light + dark), radii, and aliases onto Fluent's control resource keys.
- `Themes/OneUi/Controls.axaml` pill buttons, tonal text fields, combo boxes, pill selector (`ListBox.pills`),
                                cards (`Border.card`), status chips, toast, typography classes.

Fluent supplies behaviour, templates and accessibility; this theme only re-skins it. To change the accent colour,
edit `OneUiAccent*` in `Tokens.axaml` and the two `ColorPaletteResources` in `App.axaml`.

## Notes

- Workspaces now save to the per-user Poster folder (see `Workspace.DefaultDirectory`), not the working directory.
- Avalonia packages are pinned to `11.3.*`. Raise it deliberately if you move to a newer major version.
