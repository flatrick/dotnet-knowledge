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

The payload carries the *spelling* and never the *identity*: for an attribute hit `typeExpression`
is the whole application text, and the attribute's own type appears in no field. `kind` already says
the reference sits in an attribute position, so what is missing is what that reference resolves to.

**Carry the resolved type.** Add `attributeType` to `ApiReferenceHit`, holding the CLR name
(`System.ObsoleteAttribute`) beside the `typeExpression` that holds the source spelling
(`[System.Obsolete("…")]`). It costs nothing on other kinds — `DefaultIgnoreCondition` is already
`WhenWritingNull`.

**Resolve the suffix where it is decidable, which is inside `<AttributeName>` and nowhere else.**
A name in an attribute application can only be an attribute type, so match an `attribute` hit when
the applied name equals the symbol or the symbol equals the applied name plus `Attribute`. Do not
apply the rule to `parameter`, `return`, `base`, `interface` or `constraint`: outside an
application, `JsonConverter` means the class, and the suffix would manufacture the conflation this
item is about.

For the 539 non-colliding attribute types that is the whole fix — `System.Obsolete` names nothing
else, so the caller simply gets the hits.

**For the 78 colliding pairs, exclude the sibling's hits and name it.** A query for `Foo` returns
references to `Foo` only; applications of `FooAttribute` belong to a different type and inflating
the `attribute` total with them would give a wrong count to any caller filtering on `kind` alone.
Excluding them silently would recreate the plausible absence, so the response says what it left out
and names the call that reaches it:

```json
{ "totals": { "parameter": 25, "attribute": 0 },
  "note": { "siblingType": "System.Text.Json.Serialization.JsonConverterAttribute",
            "attributeApplications": 9,
            "remedy": "call find_api_references with …JsonConverterAttribute" } }
```

`lookup_api`'s `member_not_found` envelope is the precedent: it returns `resolvedTypes` and names
the next call rather than guessing which reading the caller meant.

The `isExact` field exists for the neighboring case, where a hit records where a reference sits
rather than what it refers to. It does not reach this one, for the reason given above.
