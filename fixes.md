USe your Umbraco backoffice skills and the Umbraco backoffice review agents create an agent team to explore this from different angles: one teammate on UX, one on technical architecture, one playing devil's advocate who is an Umbraco MVP. Use the agent team to check everything looks and feels like Umbraco that there are no console errors whe running and that everything works end to end. Also make sure the thing actually works end to end properly. Take screenshots mmake sure that visually it is iediotmatic Umbraco and works colours correct, UX patterns match Umbraco. Dont stop until we have something polished take screenshots run the tests  whatever it takes to verify it works. 

Use Umbraco MCP to set up a basic website structure in the content tree so that we can test and verify the different schemas and the razor tag helper. This thing needs to work and it needs to look professional without all the slop and failures that are there currently even though we have 84 odd tests. It needs to be in a state where the devils advocate agent could walk on stage tomorroe and demo it! 

USe your Umbraco backoffice skills and the Umbraco ux agents create an agent team to explore this from different angl
 team to check everything looks and feels like Umbraco that there are no console errors whe running and that everything
visually it is iediotmatic Umbraco and works colours correct, UX patterns match Umbraco.

Its riddled with console errors at the moment and shit frankly sort it out!

Also the preview for the JSON-LD makes no sense where it is it should be for the actual content and a content app on the content workspace view like the devliery api extensions for UMbraco the JSONLD preview should shopw the schema populated with the content. 

Also it should be using Umbraco 17.2.2

:44308/umbraco/management/api/v1/schemeweaver/mappings/blogArticle:1  Failed to load resource: the server responded with a status of 404 ()Understand this error
try-execute.controller.ts:85 [UmbTryExecuteController] Error in request: UmbApiError: HTTP 404
    at y.mapToUmbError (resource.controller.ts:40:10)
    at y.tryExecute (try-execute.controller.ts:27:26)
    at async D (tryExecute.function.ts:27:19)
    at async p (schemeweaver.repository--CNRmY2g.js:10:23)
    at async n._fetchMapping (schema-mapping-view.element-Bst87euU.js:61:17)
#t @ try-execute.controller.ts:85Understand this error
index.js:44 UUI-INPUT needs a `label` <uui-input type=​"number" id=​"versions-newer-than-days" min=​"0" placeholder=​"7" pristine>​…​</uui-input>​slot
firstUpdated @ index.js:44Understand this warning
index.js:44 UUI-INPUT needs a `label` <uui-input type=​"number" id=​"latest-version-per-day-days" min=​"0" placeholder=​"90" pristine>​…​</uui-input>​slot
firstUpdated @ index.js:44Understand this warning
:44308/umbraco/management/api/v1/schemeweaver/mappings/blogArticle:1  Failed to load resource: the server responded with a status of 404 ()Understand this error
try-execute.controller.ts:85 [UmbTryExecuteController] Error in request: UmbApiError: HTTP 404
    at y.mapToUmbError (resource.controller.ts:40:10)
    at y.tryExecute (try-execute.controller.ts:27:26)
    at async D (tryExecute.function.ts:27:19)
    at async p (schemeweaver.repository--CNRmY2g.js:10:23)
    at async n._fetchMapping (schema-mapping-view.element-Bst87euU.js:61:17)
#t @ try-execute.controller.ts:85Understand this error
:44308/umbraco/management/api/v1/schemeweaver/mappings/blogArticle/auto-map?schemaTypeName=:1  Failed to load resource: the server responded with a status of 400 ()Understand this error
try-execute.controller.ts:85 [UmbTryExecuteController] Error in request: UmbApiError: {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"schemaTypeName":["The schemaTypeName field is required."]},"traceId":"00-867db98b196b268c888e3e33441747da-b62b47cb595d9e19-00"}
    at y.mapToUmbError (resource.controller.ts:40:10)
    at y.tryExecute (try-execute.controller.ts:27:26)
    at async D (tryExecute.function.ts:27:19)
    at async p (schemeweaver.repository--CNRmY2g.js:10:23)
    at async n._handleAutoMap (schema-mapping-view.element-Bst87euU.js:84:19)

    schemeweaver.repository--CNRmY2g.js:13  POST https://localhost:44308/umbraco/management/api/v1/schemeweaver/mappings 400 (Bad Request)
(anonymous) @ schemeweaver.repository--CNRmY2g.js:13
p @ schemeweaver.repository--CNRmY2g.js:24
saveMapping @ schemeweaver.repository--CNRmY2g.js:60
saveMapping @ schemeweaver.repository--CNRmY2g.js:124
_handleSave @ property-mapping-modal.element-DKZFhgBa.js:83
handleEvent @ lit-html.js:6Understand this error
try-execute.controller.ts:85 [UmbTryExecuteController] Error in request: UmbApiError: {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"dto":["The dto field is required."],"$.contentTypeKey":["The JSON value could not be converted to System.Guid. Path: $.contentTypeKey | LineNumber: 0 | BytePositionInLine: 53."]},"traceId":"00-8767d00eefbef5070c1fe730968145c4-d76be94a1c054dca-00"}
    at y.mapToUmbError (resource.controller.ts:40:10)
    at y.tryExecute (try-execute.controller.ts:27:26)
    at async D (tryExecute.function.ts:27:19)
    at async p (schemeweaver.repository--CNRmY2g.js:10:23)
    at async o._handleSave (property-mapping-modal.element-DKZFhgBa.js:83:7)




