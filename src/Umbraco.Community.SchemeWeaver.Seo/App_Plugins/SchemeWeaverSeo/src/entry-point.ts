import type { UmbEntryPointOnInit, UmbEntryPointOnUnload } from '@umbraco-cms/backoffice/extension-api';
import { umbExtensionsRegistry } from '@umbraco-cms/backoffice/extension-registry';
import { SchemeWeaverSeoContext } from './context/seo.context.js';
import { CORE_JSONLD_VIEW_ALIAS } from './constants.js';

/**
 * Provides the single SEO context at the app root and, unless the site opted out
 * (SchemeWeaver:Seo:HideDeveloperJsonLdTab=false), hides the core's developer "JSON-LD" tab on
 * content items in favour of this package's "Schema" tab. `exclude` filters the alias whether
 * the core bundle registered before or after us, so there is no load-order race.
 */
export const onInit: UmbEntryPointOnInit = (host) => {
  const context = new SchemeWeaverSeoContext(host);

  void context
    .loadServerContext()
    .then((serverContext) => {
      if (serverContext?.hideDeveloperJsonLdTab !== false) {
        umbExtensionsRegistry.exclude(CORE_JSONLD_VIEW_ALIAS);
      }
    })
    .catch(() => {
      // A failed context call (no section permission, offline) keeps both tabs; harmless.
    });
};

export const onUnload: UmbEntryPointOnUnload = () => {
  // Nothing to tear down: the context is owned by the host and the exclusion is harmless.
};
