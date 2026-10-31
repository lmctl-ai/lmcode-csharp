# Open items

- No NuGet package or SDK release job is provided; consume by project reference or
  vendor a pinned commit.
- Optional host string-library catalogs and additional examples can be added
  independently; no language-specific behavior belongs in VM opcodes.
- Arbitrary C printf rounding outside the pinned vectors is not exhaustively
  certified. Keep float edge cases in the shared vector contract.
- Byte-oriented host results/errors: `VmString` preserves arbitrary bytes, but
  `ResultJson.Quote` and `Error.Message` accept only .NET Unicode strings.
  Decoding an invalid UTF-8 note or host rejection message replaces bytes with
  U+FFFD and differs from the C++ host's byte-preserving error/result protocol.
  The C# shop sample writes successful note JSON as raw bytes locally; a future
  SDK byte-oriented reporting/error API needs shared invalid-UTF-8 vectors before
  changing the public Unicode exception surface. Existing canonical 50 cases pass.
