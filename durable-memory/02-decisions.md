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
  store while C++ rejects slots owned by range loops. Rust also rejects invalid
  UTF-8 constants through its String representation; C++ preserves bytes.
- Errors retain the original source location when nested callbacks rethrow them.
- Library package references are absent. IsAotCompatible enables trim/AOT analyzers;
  Greeter publishes with AOT and warnings as errors.
- Optional `LmCode.Stdlib.StringCatalog` registers only `str_len(string) -> int`; lengths count raw bytes including NUL and invalid UTF-8, with schema/compiler parity and compiled-fixture tests.
