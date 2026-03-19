using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Generates Umbraco content types from Schema.org type definitions.
/// Maps schema properties to appropriate Umbraco property editors.
/// </summary>
public partial class ContentTypeGenerator : IContentTypeGenerator
{
    private readonly IContentTypeService _contentTypeService;
    private readonly IDataTypeService _dataTypeService;
    private readonly ISchemaTypeRegistry _registry;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly ILogger<ContentTypeGenerator> _logger;

    /// <summary>
    /// Maps Schema.org property types to Umbraco property editor aliases.
    /// </summary>
    private static readonly Dictionary<string, string> EditorMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Text"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.TextBox,
        ["String"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.TextBox,
        ["URL"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.TextBox,
        ["Uri"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.TextBox,
        ["Date"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.DateTime,
        ["DateTime"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.DateTime,
        ["Number"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.Integer,
        ["Int32"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.Integer,
        ["Integer"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.Integer,
        ["Boolean"] = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.Boolean,
    };

    /// <summary>
    /// Groups for organising properties on the document type.
    /// </summary>
    private static readonly Dictionary<string, string> PropertyGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "Content",
        ["headline"] = "Content",
        ["description"] = "Content",
        ["articleBody"] = "Content",
        ["image"] = "Content",
        ["url"] = "SEO",
        ["datePublished"] = "Metadata",
        ["dateModified"] = "Metadata",
        ["author"] = "Metadata",
        ["inLanguage"] = "Metadata",
        ["keywords"] = "SEO",
    };

    public ContentTypeGenerator(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        ISchemaTypeRegistry registry,
        IShortStringHelper shortStringHelper,
        ILogger<ContentTypeGenerator> logger)
    {
        _contentTypeService = contentTypeService;
        _dataTypeService = dataTypeService;
        _registry = registry;
        _shortStringHelper = shortStringHelper;
        _logger = logger;
    }

    public Guid GenerateContentType(ContentTypeGenerationRequest request)
    {
        var schemaType = _registry.GetType(request.SchemaTypeName);
        if (schemaType == null)
            throw new ArgumentException($"Schema type '{request.SchemaTypeName}' not found.");

        var existing = _contentTypeService.Get(request.DocumentTypeAlias);
        if (existing != null)
            throw new InvalidOperationException($"Content type '{request.DocumentTypeAlias}' already exists.");

        var contentType = new ContentType(_shortStringHelper, -1)
        {
            Alias = request.DocumentTypeAlias,
            Name = request.DocumentTypeName,
            Description = $"Generated from Schema.org type '{request.SchemaTypeName}'",
            Icon = "icon-science",
            AllowedAsRoot = false,
        };

        var schemaProperties = _registry.GetProperties(request.SchemaTypeName);
        var selectedProperties = schemaProperties
            .Where(p => request.SelectedProperties.Count == 0
                     || request.SelectedProperties.Contains(p.Name, StringComparer.OrdinalIgnoreCase));

        var sortOrder = 0;
        foreach (var schemaProp in selectedProperties)
        {
            var editorAlias = ResolveEditorAlias(schemaProp.PropertyType);
            var dataType = FindDataType(editorAlias);
            if (dataType == null)
            {
                _logger.LogWarning("No data type found for editor {Editor}, skipping property {Property}",
                    editorAlias, schemaProp.Name);
                continue;
            }

            var groupName = PropertyGroups.GetValueOrDefault(schemaProp.Name, request.PropertyGroupName);

            var propertyType = new PropertyType(_shortStringHelper, dataType)
            {
                Alias = ToCamelCase(schemaProp.Name),
                Name = ToFriendlyName(schemaProp.Name),
                Description = $"Schema.org: {schemaProp.Name} ({schemaProp.PropertyType})",
                SortOrder = sortOrder++,
            };

            contentType.AddPropertyType(propertyType, groupName);
        }

        _contentTypeService.Save(contentType);

        _logger.LogInformation("Generated content type '{Alias}' from Schema.org type '{SchemaType}' with {Count} properties",
            request.DocumentTypeAlias, request.SchemaTypeName, sortOrder);

        return contentType.Key;
    }

    private string ResolveEditorAlias(string schemaPropertyType)
    {
        // Strip nullable marker
        var typeName = schemaPropertyType.TrimEnd('?');

        // Check if it's a known type
        if (EditorMapping.TryGetValue(typeName, out var editor))
            return editor;

        // Default to textbox for unknown types
        return Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.TextBox;
    }

    private IDataType? FindDataType(string editorAlias)
    {
        var dataTypes = _dataTypeService.GetByEditorAlias(editorAlias);
        return dataTypes.FirstOrDefault();
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string ToFriendlyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        // Insert spaces before capitals: "articleBody" -> "Article Body"
        var result = CamelCaseSplitRegex().Replace(name, " $1");
        return char.ToUpperInvariant(result[0]) + result[1..];
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex CamelCaseSplitRegex();
}
