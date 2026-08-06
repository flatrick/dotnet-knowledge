# `find_api_references` reads attribute applications in C# short form

ECMA XML records an attribute application the way C# spells it, with the `Attribute` suffix elided
and the type otherwise fully qualified:

```xml
<AttributeName Language="C#">[System.Obsolete("Do not call or override this method.")]</AttributeName>
<AttributeName Language="C#">[System.Text.Json.Serialization.JsonConverter(typeof(…))]</AttributeName>
```

`ApiDocsQueryService.ReferencesType` matches on type-name boundaries, and neither `[` nor `(`
continues a name, so the string that matches is the short form. The tool therefore answers a
question about `System.Obsolete` when the type is `System.ObsoleteAttribute`.

This cuts both ways, and the second direction is the more serious one.

**A query naming the real type finds nothing.** `find_api_references("System.ObsoleteAttribute")`
returns 0 attribute hits; `find_api_references("System.Obsolete")` returns 1349. The caller who
spelled the type correctly is told it is unused.

**A query naming the short form finds hits that belong to a different type.** Where a namespace
holds both `Foo` and `FooAttribute`, an application of `FooAttribute` is reported as a reference to
`Foo`. `find_api_references("System.Text.Json.Serialization.JsonConverter")` reports attribute hits
that are applications of `JsonConverterAttribute` — a different type, whose only relation to the
abstract converter base class is the name.

## Why it matters

The first direction is a plausible absence, which is the failure mode this server is built to
avoid: nothing about a 0-hit answer looks like an error.

The second is worse, because it is a wrong answer rather than a missing one, and `isExact` does not
rescue it. `typeExpression` for an attribute hit is the whole application text, so `isExact` is
false for both readings and cannot separate them.

An agent has no reason to suspect either. It writes `[Obsolete]` in source and reasonably searches
for `ObsoleteAttribute`, or reads `JsonConverter` in a payload and reasonably concludes the abstract
class is meant. Requiring the caller to know that the CLR name and the source spelling differ, and
in which direction, defeats the point of the tool.

## Evidence

Against the pinned `dotnet-api-docs`:

- 617 `*Attribute.xml` documents. **78 of them sit in a namespace that also holds the de-suffixed
  type**, so the ambiguity is not a corner case: `JsonConverter`, `TypeConverter`, `EventSource`,
  `LoggerMessage`, `Switch`, `XmlSchema`, `LoaderOptimization`, and 71 more.
- Exactly one `Attribute` is elided, never more: `[System.Xml.Serialization.XmlAttribute(…)]` is the
  application of `XmlAttributeAttribute`, and `[System.Composition.MetadataAttribute]` is the
  application of `MetadataAttributeAttribute`.
- Every application carries an F# sibling element in `[<…>]` form. Only the C# element is read.

The probe that produced the collision count is a throwaway; the count is reproducible by comparing
each `*Attribute.xml` against a de-suffixed sibling in the same directory.

## Suggested fix

Resolve the short form at the point the hit is built, where the element is known to be an attribute
application, rather than by rewriting the query. Inside `<AttributeName>` a name can only be an
attribute type, so the reading is decidable there and nowhere else:

- Match an `attribute` hit when the applied name equals the symbol **or** the symbol equals the
  applied name plus `Attribute`. That closes the absence.
- Report the resolved type rather than the spelling, so an application of `JsonConverterAttribute`
  is a hit for `JsonConverterAttribute` and not for `JsonConverter`. That closes the wrong answer.

Both changes are confined to the `attribute` kind. Do not apply the suffix rule to `parameter`,
`return`, `base`, `interface` or `constraint`: outside an attribute application, `JsonConverter`
means the class, and the suffix would manufacture the conflation this item is about.

Whether the payload should also carry the applied spelling alongside the resolved type is open. It
is the only thing that would let a caller see that the two differ.

The `isExact` field exists for the neighboring case, where a hit records where a reference sits
rather than what it refers to. It does not reach this one, for the reason given above.
