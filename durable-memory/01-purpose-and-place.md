# Purpose and place

The native host owns process state and domain objects; compiled `_lmcode_` chunks
supply typed configuration and sequencing. Schema 2 names host functions, methods,
objects, overloads and conversions. The compiler lowers inline functions,
collections, operators and enums to existing VM operations. No schema interpreter
or compiler is included. Reference siblings are lmcode-cpp and lmcode-rust.
