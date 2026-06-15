using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.SchemeWeaver.AI.Services;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.AI;

/// <summary>
/// Registers SchemeWeaver AI services, controller, and Copilot tools with Umbraco.
/// Running after <see cref="SchemeWeaverComposer"/> ensures the AI override for
/// <see cref="ISchemaAutoMapper"/> wins — the last registration takes precedence.
/// </summary>
[ComposeAfter(typeof(Umbraco.Community.SchemeWeaver.Composing.SchemeWeaverComposer))]
public class SchemeWeaverAIComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(SchemeWeaverAIComposer).Assembly);

        builder.Services.AddScoped<IAISchemaMapper, AISchemaMapper>();

        // Register the concrete heuristic mapper so it can be resolved as the
        // fallback dependency by AiSchemaAutoMapper, independently of the interface.
        builder.Services.AddScoped<SchemaAutoMapper>();

        // Override the main package's ISchemaAutoMapper registration with the AI-backed
        // implementation. Because this composer runs after SchemeWeaverComposer
        // (enforced by [ComposeAfter]), this registration is the one that wins.
        builder.Services.AddScoped<ISchemaAutoMapper>(sp =>
            new AiSchemaAutoMapper(
                sp.GetRequiredService<IAISchemaMapper>(),
                sp.GetRequiredService<SchemaAutoMapper>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AiSchemaAutoMapper>>()));
    }
}
