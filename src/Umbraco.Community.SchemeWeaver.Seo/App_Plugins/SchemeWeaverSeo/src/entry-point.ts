import type { UmbEntryPointOnInit, UmbEntryPointOnUnload } from '@umbraco-cms/backoffice/extension-api';
import { umbExtensionsRegistry } from '@umbraco-cms/backoffice/extension-registry';
import { UMB_CURRENT_USER_CONTEXT } from '@umbraco-cms/backoffice/current-user';
import { SchemeWeaverSeoContext } from './context/seo.context.js';
import { CORE_JSONLD_VIEW_ALIAS, SEO_SECTION_ALIAS } from './constants.js';

/**
 * Provides the single SEO context at the app root and, unless the site opted out
 * (SchemeWeaver:Seo:HideDeveloperJsonLdTab=false), hides the core's developer "JSON-LD" tab on
 * content items in favour of this package's "Schema" tab. `exclude` filters the alias whether
 * the core bundle registered before or after us, so there is no load-order race.
 */
export const onInit: UmbEntryPointOnInit = (host) => {
  const context = new SchemeWeaverSeoContext(host);

  // Users without the SEO section would get a 403 from the server-context call, so only ask
  // once the current user is known to have it; everyone else keeps the developer tab.
  context.consumeContext(UMB_CURRENT_USER_CONTEXT, (userContext) => {
    if (!userContext) return;
    context.observe(
      userContext.allowedSections,
      (sections) => {
        if (!sections?.includes(SEO_SECTION_ALIAS)) return;
        void context
          .loadServerContext()
          .then((serverContext) => {
            // Only an explicit server answer hides the developer tab; a failed call keeps both.
            if (serverContext?.hideDeveloperJsonLdTab === true) {
              umbExtensionsRegistry.exclude(CORE_JSONLD_VIEW_ALIAS);
            }
          })
          .catch(() => {
            // A failed context call keeps both tabs; harmless.
          });
      },
      '_seoSectionAccess',
    );
  });
};

export const onUnload: UmbEntryPointOnUnload = () => {
  // Nothing to tear down: the context is owned by the host and the exclusion is harmless.
};
