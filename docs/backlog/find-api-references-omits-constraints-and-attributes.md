# `find_api_references` omits generic constraints and attribute usages

`ApiDocsQueryService.ReadReferenceHits` reads four places a type can be used: the root `<Base>`, the
root `<Interfaces>`, each member's `<Parameters>`, and each member's `<ReturnValue>`. Two other
structural uses exist in the same documents and are not read.

**Generic constraints.** A constraint names a type in a `<TypeParameter>`, not in `<Base>`:

```xml
<TypeParameters>
  <TypeParameter Name="TContext">
    <Constraints>
      <ParameterAttribute>DefaultConstructorConstraint</ParameterAttribute>
      <BaseTypeName>System.Text.Json.Serialization.JsonSerializerContext</BaseTypeName>
    </Constraints>
  </TypeParameter>
</TypeParameters>
```

`where TContext : JsonSerializerContext` is a structural use of `JsonSerializerContext`, and it is
invisible to the tool. Members carry `<TypeParameters>` too, so generic methods are affected as well
as generic types.

**Attribute usages.** `[JsonConverter(typeof(SomeConverter))]` uses `SomeConverter`, and
`<Attributes>` is not walked at all.

## Why it matters

The tool's answer to "what uses this type" is complete for the four kinds it reads and silently
short of two others. A caller cannot tell the difference between "nothing constrains a type
parameter to this" and "constraints were never examined", which is the same shape as every other
plausible absence this server is built to avoid.

Constraints are the more valuable of the two: a constraint is how an API says a type is a required
capability, which is exactly the relationship someone asking "what uses this" wants.

## Evidence

`<Constraints><BaseTypeName>` occurs in `dotnet-api-docs`, confirmed in
`xml/System.Text.Json/JsonSerializerOptions.xml`. Neither element path appears in
`ReadReferenceHits`, and no test covers either.

## Suggested fix

Walk `<TypeParameters>/<TypeParameter>/<Constraints>/<BaseTypeName>` and `<Attributes>`, at both type
and member level, with `ReferencesType` unchanged — the matching rule already handles the compound
forms these carry.

Both want their own `kind` rather than being folded into `base`: a constraint is not a base class,
and treating them alike would repeat the conflation described in
[the kind field's other ambiguity](api-reference-kind-does-not-separate-exact-from-parameterized.md).
Adding kinds is a payload change, so it belongs with a decision about whether `kind` stays a flat
enumeration.
