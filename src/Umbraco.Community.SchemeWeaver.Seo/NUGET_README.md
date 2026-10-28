# SchemeWeaver SEO

A Yoast-style structured data section for the Umbraco backoffice, built on
[SchemeWeaver](https://www.nuget.org/packages/Umbraco.Community.SchemeWeaver).

SEOs and editors pick from a curated set of content kinds Google recognises
(articles, products, events, recipes, jobs, local business, videos, courses…),
set the site identity once, and fix individual pages from a "Schema" tab, all
without touching document types, uSync or code. The developer package keeps
working alongside it on the same mappings.

## Requirements

- Umbraco 17.4+ or 18.x on .NET 10
- `Umbraco.Community.SchemeWeaver` (installed automatically as a dependency)

## Getting started

1. Install the package and restart. The **SEO Schema** section is granted to
   Administrators automatically; grant it to an "SEO" user group under
   Users &gt; User Groups.
2. **Site identity**: say who the site represents (organisation or person),
   add the logo and social profiles.
3. **Content types**: pick a kind for each page type and confirm which fields
   feed it. The wizard pre-fills from SchemeWeaver's auto-mapper (and from the
   TypeSafe or AI satellites when installed).
4. Open any page's **Schema** tab to check what it emits, switch its page type,
   or exclude it.

Documentation: https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/blob/main/docs/seo-edition.md
