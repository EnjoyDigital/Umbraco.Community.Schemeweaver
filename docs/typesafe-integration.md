# TypeSafe Integration

SchemeWeaver offers an optional companion package, **Umbraco.Community.SchemeWeaver.TypeSafe**, that replaces the auto-mapper's name matching with calibrated judgments from [TypeSafe](https://typesafe.ai) System One (the model is called Jev). When installed and given an API key, the existing **auto-map** step in the property mapping modal, and the MCP server's `suggest-property-mappings` tool, return TypeSafe suggestions instead of heuristic ones. Nothing changes in the UI.

If the package is not installed, or has no API key, SchemeWeaver works exactly as before: whichever auto-mapper was registered first (the heuristic, or the [AI satellite](ai-integration.md)) handles all suggestions.

---

## What it is

The built-in auto-mapper binds content properties to Schema.org properties by matching names (exact, synonym, substring) and assigns a fixed confidence per tier (100, 80, 50). Those numbers describe how the name matched, not how likely the mapping is to be right.

TypeSafe System One is a different kind of model from a chat LLM. It does not write text. Code hands it some **state** (here: a description of the content type) and a set of small typed **questions**, and it answers each with a probability distribution over options the code supplied:

- **Choice**: pick one option out of a closed set (at most 255 options).
- **Noul**: the probability that a condition holds.
- **Score**: a position on a short ordered scale.

Answers come back in around 100 ms, and the model can only select from what it was offered. It cannot invent a property alias or a Schema.org property that does not exist, so there is no JSON to parse, no hallucinated type names to filter out, and no salvage routine. The confidence on each answer is calibrated: it reflects how concentrated the probability is on the chosen option, not a tier in a lookup table.

SchemeWeaver uses this to ask the questions a name matcher cannot answer ("does `standfirst` supply `description`?", "is `locationName` a Place to nest, or a plain string?") while every rule about what is structurally allowed stays in code.

## Why choose it

| | Heuristic (built in) | .AI satellite | TypeSafe satellite |
|---|---|---|---|
| Dependency | None | Umbraco.AI plus a chat provider package and a configured profile | One HTTP endpoint and an API key; no AI framework |
| Output | Name matches with tier scores | Free-form LLM JSON, parsed and validated | Selections from closed option sets; nothing to parse |
| Failure mode | None (deterministic) | Malformed JSON or invented types are filtered; falls back to the heuristic | Unreachable or erroring API falls back to the prior mapper |
| Confidence | Fabricated per tier (100 / 80 / 50) | LLM self-reported | Calibrated probability per answer |
| Latency | Instant | Seconds per content type | ~100 ms per request; a content type takes a handful of requests |
| Cost | Free | Per provider token pricing | $42 per billion input tokens, roughly $0.0018 per content type |

Choose TypeSafe when you want better-than-heuristic suggestions without adopting an AI framework, with confidence numbers you can trust as thresholds. Choose the .AI satellite when you also want the AI Analyse entity actions and Copilot tools it adds to the backoffice.

---

## Requirements

| Requirement | Version |
|---|---|
| Umbraco | **17 and 18**: the satellite's version tracks the Umbraco major (17.x for Umbraco 17, 18.x for Umbraco 18) |
| Umbraco.Community.SchemeWeaver | Same version as the TypeSafe package |
| A TypeSafe API key | From [typesafe.ai](https://typesafe.ai) |

The satellite has no dependency beyond the core package and `HttpClient`. There is no .NET SDK for TypeSafe; the package speaks the HTTP API directly.

---

## Installation

Install the satellite package into the same project as SchemeWeaver:

```bash
dotnet add package Umbraco.Community.SchemeWeaver.TypeSafe
```

NuGet resolves the build matching your Umbraco major automatically, the same way the core package does.

### Get a key

Sign up at [typesafe.ai](https://typesafe.ai) and create an API key. Keep it out of `appsettings.json`: use user-secrets in development and an environment variable (or your host's secret store) in production.

### Configure

Only the key is required. With user-secrets:

```bash
dotnet user-secrets set "SchemeWeaver:TypeSafe:ApiKey" "your-key"
```

Or as an environment variable:

```bash
SchemeWeaver__TypeSafe__ApiKey=your-key
```

Every other setting has a working default. The full `SchemeWeaver:TypeSafe` section:

```json
{
  "SchemeWeaver": {
    "TypeSafe": {
      "Enabled": true,
      "Endpoint": "https://api.typesafe.ai/v1/systemone",
      "Model": "jev-latest",
      "Timeout": "00:00:30",
      "MaxRetries": 4,
      "MaxQuestionsPerRequest": 12,
      "BeamWidth": 3,
      "MaxDescentDepth": 5,
      "MaxOptionsPerChoice": 254,
      "MinBindingConfidence": 0,
      "PriorsMode": "None",
      "SecondaryBindingMinProbability": 0.30,
      "IncludeSampleValues": false,
      "MaxStateCharacters": 100000,
      "EnableCrossNodeSources": true,
      "NeighbourhoodDiscovery": "Both",
      "MaxAncestorDepth": 3,
      "MaxNeighbourTypes": 12,
      "MaxPropertiesPerNeighbour": 30,
      "MaxNeighbourProperties": 120,
      "MaxSampledNodes": 25,
      "MinObservedShare": 0.5,
      "MaxCrossNodeQuestions": 12,
      "MinCrossNodeConfidence": 60,
      "RoutesMode": "Auto",
      "MaxBlockRouteDepth": 3
    }
  }
}
```

| Key | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master switch. `false` leaves the package installed but inert; every auto-map call goes to the prior mapper. |
| `ApiKey` | `null` | Bearer token for the TypeSafe API. No key means the satellite is inert. Set via user-secrets or `SchemeWeaver__TypeSafe__ApiKey`, never appsettings. |
| `Endpoint` | `https://api.typesafe.ai/v1/systemone` | The System One endpoint. Only change it for a proxy or a test double. |
| `Model` | `jev-latest` | Model alias sent on every request. `jev-latest` tracks TypeSafe's stable release; pin an exact id (for example `jev-1.13.0`) for reproducible suggestions. |
| `Timeout` | `00:00:30` | Per-request HTTP timeout. Jev answers in around 100 ms; this only guards a hung socket. |
| `MaxRetries` | `4` | Retries with exponential backoff for HTTP 429 (rate limit) and 529 (overloaded) only. Any other failure is not retried. |
| `MaxQuestionsPerRequest` | `12` | Questions batched into one request. Questions in a request share the state and are evaluated in parallel; a wide content type against a large schema type is chunked into several requests. |
| `BeamWidth` | `3` | Beam width for the nested-type descent down the Schema.org tree. `1` is greedy and cannot recover from an early wrong turn. |
| `MaxDescentDepth` | `5` | Maximum levels the nested-type descent walks below a property's declared range. |
| `MaxOptionsPerChoice` | `254` | Options offered per Choice question. The API allows 255; one slot is reserved for the "none of these" option every binding question carries. |
| `MinBindingConfidence` | `0` | Minimum calibrated confidence (0 to 100) for a property binding to be kept at all. `0` keeps everything and lets the core auto-mapper thresholds decide what is shown. |
| `PriorsMode` | `None` | What happens to the heuristic's own suggestions once TypeSafe has answered. `None` emits TypeSafe's rows only (the best-measured setting; the heuristic is still the fallback when TypeSafe is unconfigured or fails). `GapFill` adds the heuristic's rule-driven rows (popular-defaults shapes such as `FAQPage.mainEntity`, cross-piece references, exact-alias matches) only for schema properties TypeSafe left unmapped; it never overrides a TypeSafe row. See [Evidence](#evidence) for the measured trade-off. |
| `SecondaryBindingMinProbability` | `0.30` | A content property's bind answer carries a probability per schema property. A runner-up at or above this probability whose schema property nobody else claimed becomes a second row (`title` to both `headline` and `name`), shown for review at its own probability rather than the show threshold, never pre-ticked, and never for a Block List property. Costs no extra questions. |
| `IncludeSampleValues` | `false` | Opt-in: describe each scalar property with a short, HTML-stripped sample value from one published node of the type, so the model can tell a `code` textbox holding an ISBN from one holding a coupon. **Privacy note:** sample values are your editors' content and leave the site in the request; the default sends only structure. The value schema (the shape of the stored value) is always included and carries no content. Only a published node is ever read: a type with no published node gets no sample values, and draft (possibly embargoed) content never leaves the site. |
| `MaxStateCharacters` | `100000` | Budget, in characters of JSON, for the shared request state (properties, value schemas, sample values and nested block structure; the neighbourhood attached to the cross-node round is not counted). A request is capped at 64k tokens, 32k of them for the state, and one over the cap is refused with a 4xx that is never retried, after which the whole mapping falls back to the prior mapper. While the state is over budget, detail is dropped in a fixed order until it fits: block-field value schemas, then nested block levels beyond the first, then the properties' own value schemas, then sample values. Each step is logged at Debug with sizes only. The default is roughly 25k tokens at four characters a token. |
| `EnableCrossNodeSources` | `true` | Master switch for the cross-node round (`parent`, `ancestor` and `sibling` sources). |
| `NeighbourhoodDiscovery` | `Both` | How the types above and beside this one are found. `Structure` reads the document types' allowed-children declarations; `Observed` samples real pages of the type and reads their actual parents, ancestors and siblings (what works on sites whose structures are loose, which is most of them); `Both` merges the two, structure first. |
| `MaxAncestorDepth` | `3` | How far above the type ancestors are collected (`1` is parents only). |
| `MaxNeighbourTypes` | `12` | Neighbour types offered, parents first, then ancestors nearest-first, then siblings. |
| `MaxPropertiesPerNeighbour` | `30` | Properties listed per neighbour type. |
| `MaxNeighbourProperties` | `120` | Neighbour properties offered in one cross-node Choice in total (the API allows 255 options). |
| `MaxSampledNodes` | `25` | Pages of the type sampled for `Observed` discovery. |
| `MinObservedShare` | `0.5` | Share of sampled pages a parent, ancestor or sibling type must appear in to count. An observed sibling type is also offered only when no sampled parent holds more than one page of it, because a `sibling` row reads the first sibling of that type at render time; a sibling declared in the document-type structure is offered regardless. |
| `MaxCrossNodeQuestions` | `12` | Unbound schema properties asked about in the cross-node round, most cross-node-prone first. |
| `MinCrossNodeConfidence` | `60` | Minimum calibrated confidence (0 to 100) for a cross-node row. The default is the core's show bar, so a cross-node row is offered like any other suggestion and pre-ticked only at the core's auto-apply bar (80). Raise it to keep cross-node rows off the screen unless the model is surer. |
| `RoutesMode` | `Auto` | When a Block List row is emitted as per-block-type `routes` rather than one nested type for the whole list. `Auto` uses routes only when the list needs them (the block types land on different Schema.org types, a block contains a nested Block List, or one block type was judged not to belong); `Always` uses them whenever a list is emitted as nested objects; `Off` emits the v1 shapes only. |
| `MaxBlockRouteDepth` | `3` | How many levels of blocks-inside-blocks are planned (the core resolves three). |

---

## What changes in the backoffice

Nothing visible. The satellite decorates SchemeWeaver's `ISchemaAutoMapper` seam, so:

- The **auto-map** step in the property mapping modal (and the bulk flows that call it) shows TypeSafe suggestions in the same table, with the same columns.
- The MCP server's `suggest-property-mappings` tool returns the same suggestions to an AI assistant.
- The core thresholds still apply: suggestions at **80 or above** are pre-ticked and applied automatically, suggestions at **60 or above** are shown for review, and anything below is hidden. These are the `SchemeWeaver:AutoMapper` settings (`AutoApplyConfidenceThreshold`, `ShowConfidenceThreshold`) and the UI behaves identically; the difference is that the numbers feeding them are now calibrated probabilities rather than tier constants.

### The health check

The package registers a backoffice Health Check named **SchemeWeaver TypeSafe** (under **Settings > Health Check**). It reports one of five outcomes:

- **Invalid configuration** (Error): an option is out of range or the endpoint is not an https URL (plain http is accepted on localhost only). The message names the option.
- **Disabled** (Info): `Enabled` is `false`.
- **Not configured** (Warning): no `ApiKey` is set, so the satellite is inert.
- **Live** (Success): a ping to the endpoint succeeded, with the model id the API answered with and the round-trip latency.
- **Unreachable or failed** (Error): the ping failed or timed out, with the HTTP status when there is one (401 means the key is wrong; 422 means a request-shape bug worth reporting).

Run it after installing, and again whenever suggestions look unchanged.

### The startup log line

On boot the satellite writes one Information line stating whether it is **active** or **inert**, and which prior auto-mapper it wraps (the heuristic `SchemaAutoMapper`, or the AI satellite's mapper when both are installed). If you are unsure whether TypeSafe is answering auto-map, this line is the first thing to check.

---

## How it works

For one content type and one target Schema.org type, the mapper runs up to six rounds of questions. Code owns every rule; the model only supplies the semantic judgments.

1. **Bind.** For each content property, "which property of the target type does this supply?" is asked as a Choice over that type's real properties plus a "none of these" option. The model can only pick from the list, so a binding to a property that does not exist on the type is impossible.
2. **Shape.** For each bound row, is the content property a distinct named entity to nest (a Person, a Place, an Offer) or a plain value? For a Block List, should each block become one nested object, or should the blocks flatten to a list of strings?
3. **Nested type.** For rows that nest, the nested Schema.org type is chosen by a beam search down the type tree, starting from the property's declared range, one Choice per level with a "stop here" default. Because the walk starts at the declared range, an out-of-range nested type cannot be expressed, and because "stop here" is always on offer, canonical types (Review, Offer, Person) are preferred over exotic subtypes.
4. **Inner.** Each block field, or each source field, is bound to a property of the nested type, again as a Choice over that type's real properties.
5. **Cross-node.** For the schema properties nothing on the page could supply (a blog post's `publisher`, a department's `location`), the model is shown the content type's neighbourhood: the document types whose pages sit above it (parents, then ancestors nearest-first) and beside it (siblings), each with its readable properties. One Choice per unbound schema property offers those neighbour properties, tagged with their relation, plus "none of these". A chosen option becomes a `parent`, `ancestor` or `sibling` row that names the neighbour type, so the saved mapping reads the right page at render time. The neighbourhood is discovered from the document-type structure (allowed children), from a sample of real pages of the type, or both (`NeighbourhoodDiscovery`), and only properties that really exist on those types are ever offered.
6. **Per-block routes.** When a Block List allows several block types, or a block itself contains another Block List (a page section holding its own FAQ items), one nested type for the whole list is the wrong shape. In that case each block type is treated as its own subject: it gets its own nested-type descent (from the same declared range), its own inner bindings, and a nested Block List field inside it gets the same treatment one level down, up to `MaxBlockRouteDepth`. The result is emitted as the core's `routes` config (one route per block alias), which the block resolver already reads. `RoutesMode: Auto` uses this only when the list needs it; a list whose blocks all land on one type keeps the simpler v1 shape.

**Multiple targets.** A content property is still asked one bind question, but the answer carries a probability per schema property. When the runner-up is at or above `SecondaryBindingMinProbability` and no other content property claimed it, it becomes a second row, which is how `title` can supply both `headline` and `name` without a second question. A runner-up rarely holds more than half of the distribution, so such a row is gated on that probability instead of the core's show threshold, is never pre-ticked (accept it with a click), and is never created for a Block List property, whose runner-up would otherwise run the whole route planner for a row nobody asked for.

### A worked example

An `eventPage` document type has `title`, `startDate`, `locationName` and `locationAddress`, and is being mapped to `Event`.

- Bind: `title` -> `name`, `startDate` -> `startDate`, `locationName` -> `location`, `locationAddress` -> `location`.
- Two content properties claimed the same schema property, so code merges them into one nested entity for `location` and asks Shape: is this a named entity? Yes.
- Nested type: `Event.location` has a declared range that includes `Place`. The descent starts there, is offered `Place`'s subtypes plus "stop here", and stops at `Place`.
- Inner: `locationName` -> `Place.name`, `locationAddress` -> `Place.address`.

The saved mapping is `Event.location` as a `complexType` of `Place` with two nested rows, and the JSON-LD contains:

```json
{
  "@type": "Event",
  "name": "Summer Fair",
  "startDate": "2026-07-04",
  "location": {
    "@type": "Place",
    "name": "Town Hall",
    "address": "1 High Street"
  }
}
```

### Rules that stay in code

- **A Block List is judged by its container family.** Which collection property a block list belongs on (`mainEntity`, `hasPart`, `about`, `itemListElement`, `mainContentOfPage`) is a secondary choice the model spreads its probability across, so a list bound to one of them carries the probability mass of the whole family rather than the winner's share alone. Without this, a fully planned routes row was dropped below the show bar because two equivalent containers split its probability.
- **Media pickers are never wrapped.** A media property already resolves to a full `ImageObject` through the media resolver; nesting it would produce an empty shell.
- **The heuristic is the fallback, not a co-author.** By default (`PriorsMode: None`) TypeSafe's rows stand alone, and the heuristic answers only when TypeSafe is unconfigured or fails. This was measured, not assumed: letting the heuristic's rule-driven rows override TypeSafe's halved rich coverage on the evaluation harness, because wrong shapes pre-empted right ones. If your site depends on the heuristic's [popular defaults](property-mappings.md#popular-schema-defaults) (for example `FAQPage.mainEntity` as a Block List of `Question`, which Google's FAQ rich result requires), set `PriorsMode: GapFill`: those shapes are then added for any schema property TypeSafe left unmapped, and never in place of a TypeSafe row.
- **Collisions become one entity.** A schema property claimed by several content properties (`locationName` + `locationAddress`) becomes a single nested entity rather than two competing rows.
- **The declared range bounds the nested type.** The descent cannot leave the property's range.
- **Confidence flows through unchanged.** The calibrated confidence (0 to 100) is what the core thresholds see. Cross-node rows have their own floor (`MinCrossNodeConfidence`), which defaults to the same show bar; a wrong cross-node guess reads the wrong page, so raise it if your editors would rather see fewer of them.
- **Every alias is real.** Neighbour types and their properties come from Umbraco's own structure (declared allowed children, or the actual pages sampled); nothing the model can select is invented, so a cross-node row always names a type and a property that exist.

### Coexistence with the .AI satellite

Both satellites sit on the same `ISchemaAutoMapper` seam, and the order is fixed: the AI composer replaces the seam and implements the core's `ISchemaAutoMapperReplacingComposer` marker, and the TypeSafe composer is declared to run after every composer that does, so it always wraps the AI mapper. With both installed you get a cascade: TypeSafe answers, the AI mapper is its fallback, and the heuristic is the AI mapper's fallback. `Enabled: false` is the explicit off switch for TypeSafe. A third-party composer that replaces the seam without the marker would still take it; the startup log line names whichever mapper won, so check it rather than guessing.

The .AI satellite's entity actions (AI Analyse, AI Analyse All) and Copilot tools are unaffected either way.

### Programmatic schema-type suggestion

The package also registers `ITypeSafeSchemaTypeSuggester`, which suggests a Schema.org type for a content type by the same beam search, starting from `Thing`. It returns up to three candidates with calibrated confidence and the descent path that reached each (for example `Thing > CreativeWork > Article > BlogPosting`). It is not wired to any UI in this version; inject it where you need it.

---

## Limits

- **Text only, structure by default.** Jev accepts text input. The model sees the content type's structure and each property's value schema (the shape of the stored value), never its values, unless `IncludeSampleValues` is switched on, and then only one short sample per scalar property, read from a published node only (a type with no published node sends no samples).
- **Depth caps.** Ancestors are collected up to `MaxAncestorDepth` levels, blocks-inside-blocks are planned up to `MaxBlockRouteDepth` levels, and the nested-type descent stops at `MaxDescentDepth`. Deeper structure is not seen. A state wider than `MaxStateCharacters` is thinned before it is sent (block-field value schemas first, then nested block levels beyond the first, then property value schemas, then sample values), so a very wide type may be described in less detail than these caps allow; the Debug log says what was dropped.
- **What a cross-node row reads at render time.** A `parent` mapping reads whichever page is the actual parent of the page being rendered, regardless of the type the suggestion named; an `ancestor` mapping reads the nearest ancestor of that type that has a value for the property; a `sibling` mapping reads the first sibling of that type that has a value. Because a `parent` row ignores the type, the satellite emits a `parent` row only when the neighbourhood has a single parent type; when pages of the type live under several parent types, the chosen parent is emitted as an `ancestor` row naming that type, which reads the actual parent when it is that type and never a parent of another type.
- **A neighbourhood needs structure or content.** The neighbourhood is discovered from document-type structure (allowed children) and/or sampled real pages, so a brand-new site with no content and no declared allowed children gets no cross-node rows. Declare allowed children, or create a few pages, and re-run auto-map.
- **One bind question per content property.** A second target comes only from the same answer's runner-up probability (`SecondaryBindingMinProbability`); a content property that genuinely supplies three schema properties still gets at most two rows.

---

## Cost

TypeSafe bills input tokens at $42 per billion; output tokens are free. On the repository's evaluation harness, mapping one content type cost about $0.0018 (roughly 43,000 input tokens across the four v1 rounds, including the chunked binding requests). The cross-node and per-block-routes rounds add requests only for the content types that need them (unbound schema properties with a non-empty neighbourhood; block lists with several block types or nested lists), and the neighbourhood is attached to the cross-node round's state only, so the other rounds do not pay for it.

For a site with 150 document types, mapping every one of them once:

```text
150 content types x $0.0018 = $0.27
```

Re-running auto-map on a content type costs the same again, so a full-site re-map is a few tens of cents. Per-request latency is around 100 ms; a content type takes several sequential requests because each round depends on the one before it.

---

## Troubleshooting

### Suggestions look the same as before

Check the startup log line and the **SchemeWeaver TypeSafe** health check. If the line says inert, the key is missing, `Enabled` is `false`, or another auto-mapper composer ran after this one.

### The log line says inert

Either `Enabled` is `false` or no `ApiKey` was found. Confirm the secret is bound to `SchemeWeaver:TypeSafe:ApiKey` (user-secrets) or `SchemeWeaver__TypeSafe__ApiKey` (environment) and restart.

### HTTP 401

The key is wrong or revoked. The satellite falls back to the prior mapper and logs a warning; fix the key and restart.

### HTTP 422

The request shape was rejected. This is not a configuration problem; it means the package built a question the API does not accept. Please [open an issue](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/issues) with the log entry.

### HTTP 429 or 529

Rate limited or overloaded. These are retried automatically with exponential backoff up to `MaxRetries`; only if every retry fails does the call fall back to the prior mapper.

### Nothing ever breaks the page

By design. The satellite only runs when an editor or the MCP server asks for suggestions; it never runs during page rendering or JSON-LD generation. If the API is unreachable, unconfigured or returns an error, the call falls back to the prior mapper and the backoffice carries on.

---

## Evidence

The design was proven on the repository's evaluation harness (18 content types, `eval/run-typesafe.mjs`, scored against hand-written gold mappings) before the package was written. `eval/typesafe-mapper.mjs` and `eval/schema-graph.mjs` are the executable specification the C# port follows.

| Mapper | Rich-mapping coverage | Strict F1 | Lenient F1 | Cost per content type |
|---|---|---|---|---|
| TypeSafe (this package, as it emits: gated at the core show threshold) | 67% (8 of 12; an earlier run reached 75%) | 0.714 | 0.719 | $0.0018 |
| .AI satellite (Anthropic) | 83% | 0.683 | 0.705 | provider pricing |
| Heuristic, current release (measured live against the TestHost) | 75% (9 of 12) | 0.561 | 0.561 | free |
| Heuristic, June cache (for reference only) | 25% | 0.534 | 0.558 | free |

Read the heuristic row carefully: the current heuristic reaches the same rich coverage as TypeSafe, because its popular-defaults table hard-codes the common shapes, but it does so by emitting 14.6 suggestions per content type against TypeSafe's 8.9, and the padding is what the F1 gap measures. The claim to make for TypeSafe is precision (fewer, better-calibrated suggestions, and a source type chosen by meaning rather than by name), not coverage.

The harness ceiling (a hypothetical perfect model answering the same questions, `eval/oracle.mjs`) is strict F1 0.906; the shortfall is the v1 scope (no cross-node rows, no routes, one target per content property). Rich coverage moves by a row between runs (the model is not perfectly deterministic), so treat the coverage figure as approximate and the F1 figures as the stable ones.

### v2, measured live

The JS mapper stays at v1 as the frozen specification, so v2 (cross-node sources, per-block routes, multiple targets, value schema) is measured on the C# package itself: `eval/run-typesafe-live.mjs` calls the TestHost's auto-map endpoint for the same 18 content types and scores the rows the backoffice would receive. The scorer holds v2 to a stricter bar than v1 was held to: a cross-node row must also name the right source content type, and a routes row is compared route by route.

| Mapper (live, TestHost) | Rich-mapping coverage | Cross-node coverage | Strict F1 | Lenient F1 | Suggestions per type | Time per type |
|---|---|---|---|---|---|---|
| TypeSafe v2 (this package) | 67% (8 of 12) | 50% (3 of 6) | 0.581 | 0.582 | 13.8 | 2.2 s |
| TypeSafe v1 (JS harness on the cached context, not live; for reference) | 67% (8 of 12) | 0% (0 of 6) | 0.696 | 0.700 | 9.7 | n/a |
| Heuristic (same host, no key) | 75% (9 of 12) | 0% (0 of 6) | 0.561 | 0.561 | 14.6 | under 0.1 s |

Measured on 2026-09-22 against the TestHost (Umbraco 18 build, 18 content types, `jev-1.13.0`), after a uSync import of the document-type structure the listing pages now declare. What the rows show:

- **Cross-node rows exist and are right where the site's structure is explicit.** `productPage` and `vehiclePage` get `category <- parent:productListing.title`, `blogArticle` gets `publisher <- ancestor:homePage.organisationName`, `departmentPage` and `localBusinessChild` get `email` and `telephone` from their parent organisation page (pre-ticked at 96 to 99), and the rows carry the source type alias the backoffice needs. The three gold rows still missed are judgement calls the model makes differently: it declines `category` for the event listing (a parent found only by sampling the tree, not declared), it does not read a department's `location` from a sibling contact page, and it offers a shop's parent organisation as `email`, `logo` and `contactPoint` rather than `parentOrganization`.
- **Routes work end to end.** `nestedBlocksPage` gets `mainContentOfPage <- sections` as a `routes` configuration (`section` to `WebPageElement`, its nested `questions` list routed on to the FAQ item type) at 90, pre-ticked. The gold row names `hasPart`; both are valid containers on `WebPage`, and the scorer counts the difference as a miss.
- **The FAQ shape is still the heuristic's.** The model binds `faqItems` to `hasPart` as `CreativeWork` (`question` to `headline`, `answer` to `text`), a sensible reading that is not the `mainEntity`/`Question`/`Answer` shape Google's FAQ rich result needs. The heuristic's popular-defaults table encodes that shape as a rule, which is why the heuristic keeps 9 of 12 rich rows to TypeSafe's 8 and why `PriorsMode: GapFill` recovers it (75%, at the precision cost the priors table shows).
- **Two defaults were decided by this measurement.** With `MinCrossNodeConfidence` at 80 (the first v2 cut) the same run scored strict F1 0.637 with 11.5 suggestions per type but found only 1 of 6 cross-node rows; the rows editors would want to review sat at 60 to 79. At 60, the shipped default, 3 of 6 are found at 13.8 suggestions per type and strict F1 0.581. The extra rows are unticked cross-node suggestions (`occupationalCategory` and `industry` from a categories listing on `jobPostingPage`, `about` and `keywords` from the event listing), and the gold set predates cross-node mapping, so a plausible cross-node row on a "flat" type counts against precision here. And the block-list container rule (see "Rules that stay in code") exists because the first run planned the `nestedBlocksPage` routes in full and then gated the row: a 0.40/0.35 split between two equivalent containers left the winner under the show bar.
- **Cost and time.** 2.2 s per content type on average (0.9 to 5.3 s; the recipe page with three block lists is the slow end), roughly 80k input tokens per type, about $0.003 at the published rate.

The priors-merge block from the same rows, for the `PriorsMode` decision: gap-filling at the auto-apply bar gives rich 67% (8 of 12), cross-node 2 of 6, strict F1 0.573 at 14.1 suggestions per type; at the show bar, rich 75%, cross-node 2 of 6, strict F1 0.517 at 16.7. `None` stays the default; the `mergedAtShow` figure is the one to reach for when the Google-mandated FAQ shape matters more than the padding it brings.

### The priors rule, measured

The harness scores TypeSafe on its own, so the package's `PriorsMode` was scored afterwards from the same answers (the "with heuristic priors" block that `eval/run-typesafe.mjs` prints):

| `PriorsMode` (priors from the current heuristic) | Rich-mapping coverage | Strict F1 | Suggestions per type |
|---|---|---|---|
| `None` (default) | 8 of 12 | 0.714 | 8.9 |
| `GapFill` | 9 of 12 | 0.569 | 13.8 |
| Override (the rule the package was first built with: non-`property` priors at 80 or above replace TypeSafe's rows) | 8 of 12 | 0.693 | 9.6 |

Override is not offered: against the June heuristic it halved rich coverage (kept rows carried wrong shapes that pre-empted TypeSafe's correct ones), and against the current heuristic it still loses F1. `GapFill` recovers one rich row (the `FAQPage.mainEntity` block shape) but drags the heuristic's weak rows in with it, hence the precision cost; choose it when that shape matters more to you than a shorter, cleaner suggestion list.

These figures were measured against the current heuristic (the range-aware enricher that shipped in 17.10.0 / 18.5.0), scored live from the TestHost. The F1 lead over both alternatives, and the cost, are the figures to quote.
