using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.SchemeWeaver.AI.Services;

namespace Umbraco.Community.SchemeWeaver.AI;

/// <summary>
/// Registers SchemeWeaver AI services, controller, and Copilot tools with Umbraco.
/// </summary>
public class SchemeWeaverAIComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(SchemeWeaverAIComposer).Assembly);

        builder.Services.AddScoped<IAISchemaMapper, AISchemaMapper>();
    }
}
