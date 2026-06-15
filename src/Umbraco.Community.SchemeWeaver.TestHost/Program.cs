#if !UMBRACO18
using Umbraco.AI.Extensions; // AddUmbracoAI() — Umbraco.AI.Startup, 17-only
#endif

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    // Umbraco 18 no longer wires the Delivery API implicitly via AddWebsite():
    // the full registration (including IRequestSegmentService, which the
    // Delivery API's LocalizeFromAcceptLanguageHeaderAttributeFilter depends
    // on) now lives in AddDeliveryApi() and must be chained explicitly — see
    // Umbraco.Web.UI/Program.cs in 18.0.0-rc2. Without it the Delivery API
    // endpoints 500 with "Unable to resolve service for type
    // 'IRequestSegmentService'".
    .AddDeliveryApi()
#if !UMBRACO18
    // Umbraco.AI is 17-only (no Umbraco 18 build). Registers AI providers, profiles,
    // persistence, and the backoffice AI UI. The Anthropic provider is discovered
    // automatically from the Umbraco.AI.Anthropic assembly via [AIProvider] attribute.
    // UmbracoAIComposer (IDiscoverable) also calls this — the call is idempotent.
    .AddUmbracoAI()
#endif
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();


app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();

// Exposed for Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
// so integration tests can bind to this entry point. No behavioural impact.
public partial class Program;
