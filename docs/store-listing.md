# Store listing

To change what the store pages say, edit [`store/listing/en-US.md`](../store/listing/en-US.md) and
[`store/listing/cs-CZ.md`](../store/listing/cs-CZ.md), then paste the result into each store's console. The pipeline
publishes packages, not listing text.

[`store/README.md`](../store/README.md) has the field limits, the rules for program names and the disclaimer, and
the list of what still blocks a first submission (in-app account deletion, signed-off legal pages, screenshots).

`StoreListingTests` (`tests/ShowUp.Core.Tests/Packaging`) checks every listing on each build: all headings present,
every field within the tightest limit of the three stores, the description ending with the app's own disclaimer, and
no program name used as a keyword.

Related: [release-pipeline.md](./release-pipeline.md), [release-runbook.md](./release-runbook.md),
[privacy.md](./privacy.md), [terms.md](./terms.md).
