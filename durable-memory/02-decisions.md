# Decisions

- `VmString` owns a defensive copy of bytes and exposes a read-only span. C# UTF-16
  strings are not VM values. Embedded NUL and invalid UTF-8 survive constants,
  concatenation, equality and host callbacks. UTF-8 conversion is explicit.
- Schema identifiers alone are decoded with strict UTF-8; invalid identifier bytes
  receive LOAD_UNBOUND. Value constants are never decoded by the VM.
- Context owns an object arena. ObjRef is a context-local index; generic `is T`
  access needs no reflection. The host must not mix handles between contexts.
- Bound callbacks are retained at load time, matching C++; changing registries
  cannot replace an already loaded program's imports. The run registry supplies
  declared context types.
- Integer operations use explicit unchecked wrapping. MinValue / -1 returns
  MinValue and MinValue % -1 returns zero without triggering .NET overflow.
- Float formatting uses 15 significant digits, lowercase signed exponents with at
  least two digits, and explicit nan/inf/-0. Pinned formatting vectors are exact.
- Structural range-slot ownership follows C++. Rust rejects every hidden-local
  store while C++ rejects slots owned by range loops. C# preserves invalid UTF-8
  value constants as raw `VmString` bytes; host-facing text conversion remains explicit.
- Errors retain the original source location when nested callbacks rethrow them.
- Library package references are absent. IsAotCompatible enables trim/AOT analyzers;
  Greeter publishes with AOT and warnings as errors.
- Optional `LmCode.Stdlib.StringCatalog` registers the full 16-function C++ string catalog. All operations use `VmString` byte spans, preserve invalid UTF-8/NUL and use byte offsets. Case conversion is ASCII-only; trimming removes space and bytes 0x09..0x0d.
- Catalog repeat/replace output limits are 67108864 bytes; glob matching counts iterations, class entries and trailing stars toward 2^27 work units. Limits raise `RUN_HOST` with the same messages as C++ and are host policy, not VM budgets.
- Decimal parsers require a complete ASCII token with at most one sign. Integers must fit int64; floats must be finite and cannot underflow a nonzero significand to zero. Subnormals and signed zero are preserved.
- Catalog signatures are checked against the shipped schema fragment and `lmscript --dsl-signatures`. Shared compiler-built catalog vectors cover binary strings and host-policy errors.
- `CollectionsCatalog` and `TestingCatalog` provide the shared collection/testing host functions. Collection objects are tracked per `Context` with a 65,536-object budget; testing output writes raw `VmString` bytes plus LF, and catalog policy failures use `RUN_HOST`.
