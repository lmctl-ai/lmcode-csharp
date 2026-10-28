# Gotchas

- Exact schema bytes are SHA-256 bound, including whitespace and line endings.
  A `dsl.schema: 2` schema may carry a top-level `interfaces` map (compiler
  R48 rule methods); the runtime never parses it, but it joins the hash, so
  schemas compiled with and without it are not interchangeable.
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
