# Gotchas

- Exact schema bytes are SHA-256 bound, including whitespace and line endings.
- VM byte strings must not pass through UTF-16 when lossless bytes are required.
- C# evaluates left operands first: read a relative displacement before adding the
  reader position, which must point after the operand.
- No backward compatibility, full static type inference, sandbox or execution limit.
- Stack-depth verification is enabled for loop-bearing code, matching the references.
- Host callbacks report expected host errors with Error.Host; unrelated host
  exceptions remain host exceptions.
- System.Text.Json in the conformance runner uses JsonDocument, not reflection-based
  serialization. The library's JSON result helpers write explicitly escaped text.
- CPU/libm transcendental results use conformance's approximate expectations;
  formatting and ordinary exact traces are compared exactly.
