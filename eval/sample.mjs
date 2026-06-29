// The eval sample: a deliberately rich-weighted set of content types.
//
// The TestHost gold set is 867 flat `property` mappings vs only 12 self-contained
// rich (complexType/blockContent) + 19 cross-node (ancestor/sibling/parent). Flat
// mappings on clean names are exactly where the 241-entry synonym heuristic already
// wins, so a random sample would show a false tie. We therefore anchor the sample on
// the rich win-zone and add diverse flat controls to guard against regressions and to
// exercise schema-type selection.

export const SAMPLE = [
  // --- self-contained rich (blockContent / complexType): the PRIMARY metric ---
  { alias: 'recipePage', tag: 'rich' }, // 2 blockContent + 1 complexType (the hardest)
  { alias: 'howToPage', tag: 'rich' }, // 2 blockContent (Step -> HowToStep, Tool -> stringList)
  { alias: 'blogArticle', tag: 'rich' }, // complexType Author->Person (+ ancestor Publisher)
  { alias: 'eventPage', tag: 'rich' }, // complexType (Location/Organizer) + cross-node
  { alias: 'productPage', tag: 'rich' }, // blockContent + cross-node
  { alias: 'faqPage', tag: 'rich' }, // blockContent mainEntity -> Question/Answer
  { alias: 'homePage', tag: 'rich' }, // blockContent
  { alias: 'nestedBlocksPage', tag: 'rich' }, // blockContent (nested)

  // --- cross-node rich (ancestor / sibling / parent): STRETCH metric ---
  { alias: 'departmentPage', tag: 'cross' }, // sibling Location
  { alias: 'localBusinessChild', tag: 'cross' },

  // --- flat controls: diverse schema types; guard against regression + type-selection ---
  { alias: 'corporationPage', tag: 'flat' },
  { alias: 'techArticle', tag: 'flat' },
  { alias: 'newsArticle', tag: 'flat' },
  { alias: 'jobPostingPage', tag: 'flat' },
  { alias: 'coursePage', tag: 'flat' },
  { alias: 'moviePage', tag: 'flat' },
  { alias: 'restaurantPage', tag: 'flat' },
  { alias: 'vehiclePage', tag: 'flat' },
];

export const SAMPLE_ALIASES = SAMPLE.map((s) => s.alias);
