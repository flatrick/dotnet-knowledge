# `find_api_references` reports a parameterized base or interface as if it were the type

`kind` names where a reference sits, not what the type is to the declaration. A class implementing
`IComparer<string>` is an `interface` hit for `System.String`, because `System.String` occurs inside
the interface name:

```json
{"symbol":"Microsoft.Extensions.Configuration.ConfigurationKeyComparer",
 "kind":"interface",
 "typeExpression":"System.Collections.Generic.IComparer<System.String>"}
```

Against the pinned corpus, `System.String` reports `base: 11` and `interface: 85` — for a sealed type
that cannot be derived from or implemented. Every one of those is a generic argument.

## Why it matters

`interface: 85` reads as "85 types implement System.String", which is false and, for a sealed type,
obviously so. For a type where it is *not* obvious — `System.IDisposable` reports 499 interface hits,
some exact and some parameterized — a caller has no way to tell the two apart without string-matching
`typeExpression` against the symbol themselves.

The payload is honest: `typeExpression` carries the interface as declared, so the information is
present. It is the labeling that invites the wrong reading, and the tool description currently
carries the warning that the schema should.

`parameter` and `return` do not have this problem in the same way. A method taking
`IEnumerable<string>` genuinely does take a string-shaped thing, and callers reason about parameters
compositionally. Inheritance is the relationship where "is it exactly this" is the question being
asked.

## Evidence

Confirmed in the corpus: `<InterfaceName>System.Collections.Generic.IComparer&lt;System.String&gt;`
in `Microsoft.Extensions.Configuration/ConfigurationKeyComparer.xml`, and
`IEnumerable<KeyValuePair<System.String,System.String>>` in
`Microsoft.Extensions.Configuration.Memory/MemoryConfigurationProvider.xml`.

`ApiDocsQueryService.ReferencesType` is doing exactly what it should here — the same rule is what
makes `string[]` and `out string` findable as parameters. The gap is that the result does not record
which case it was.

## Suggested fix

Add a boolean to `ApiReferenceHit` — `isExact`, true when `typeExpression` equals the symbol — so a
caller filters instead of parsing. It is one comparison at the point where the hit is already
constructed and the data is already in hand.

Filtering on it would then be worth exposing too, since "what derives from Stream" and "what has a
base parameterized by Stream" are different questions.
