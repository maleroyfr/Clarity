# Discovery ZIP v1

The four root files are UTF-8 JSON (no comments, duplicate keys or non-finite
numbers). JSONL files contain one nonempty JSON object per line; empty files mean
no observations. Dates are ISO 8601 UTC (`Z` or `+00:00`). IDs remain strings.
`schemaVersion` is `1.minor`; unknown majors are rejected. Schemas are embedded in
Clarity.Contracts and evaluated offline with JsonSchema.Net before deserialization.

`scope` is `{ "kind": "tenant|forest|domain|ou|resource", "value": "..." }`.
The manifest records the requested scope; each result records the verified scope.
`sectionId` uniquely identifies a comparable scope unit, while `capabilityId`
maps it to a stable catalog key. Two domains therefore need separate section IDs.
Two results for the same capability and scope in one package are invalid.

Objects and relationships reference exactly one Complete/Partial result. Attribute
keys and relationship types must be declared as covered. Counts are the number of
records actually present, not an estimate of missing objects. Complete zero is
known zero **only within that verified scope and covered fields**. Failed and
NotApplicable counts may be absent/null (unknown); references outside scope remain
unresolved and may carry `properties.outOfScope: true`. Never infer a deletion from
a partial, failed or differently scoped run.

An absent attribute was not supplied. Explicit null means an observed empty scalar;
`""` is an observed empty string and `[]` an observed empty list. Field definitions
in the catalog narrow these values further. Reliable volume fields end in `Bytes`
and contain nonnegative 64-bit integer byte counts or null; do not invent a volume
when the producer cannot measure it. Alternative keys have the same typed reference
shape as relationship endpoints.

Complete sections cannot contain errors. Partial/Failed sections include at least
one sanitized warning/error. Messages must not contain credentials, raw service
responses or secret values. Content of messages/files, passwords, tokens and
recovery keys are outside this metadata-only contract. The synthetic example is a
partial AD package, not a claim of live AD validation.

Structural validation is distinct from authorization: the receiving service must
check project, actual environment, optional request, catalog capability and scope
before confirmation or publication. ZIP safety/size limits are enforced by the
ingestion boundary, not by JSON Schema.
