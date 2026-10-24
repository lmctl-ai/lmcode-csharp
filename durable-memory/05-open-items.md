# Open items

- No NuGet package or SDK release job is provided; consume by project reference or
  vendor a pinned commit.
- Optional host string-library catalogs and additional examples can be added
  independently; no language-specific behavior belongs in VM opcodes.
- Arbitrary C printf rounding outside the pinned vectors is not exhaustively
  certified. Keep float edge cases in the shared vector contract.
