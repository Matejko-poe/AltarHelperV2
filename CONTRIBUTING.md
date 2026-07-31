# Contributing

Thanks for helping improve Altar Helper V2.

## Bug reports

Include your ExileCore version, Path of Exile version, reproduction steps, `Errors.txt`, and a screenshot when the issue is visual. Never include account credentials or private tokens.

## Pull requests

1. Keep changes focused and explain the user-facing effect.
2. Build against a current ExileCore installation:
   `dotnet build -p:exapiPackage="C:\path\to\ExileCore"`.
3. Test altar parsing and overlays in game where possible.
4. Update `README.md` and `CHANGELOG.md` for user-visible changes.

Modifier data should use normalized `#` placeholders for numeric values and identify its target as `Player`, `Minion`, or `Boss`.
