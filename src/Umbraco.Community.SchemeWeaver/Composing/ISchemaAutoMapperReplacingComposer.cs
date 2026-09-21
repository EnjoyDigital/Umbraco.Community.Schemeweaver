using Umbraco.Cms.Core.Composing;

namespace Umbraco.Community.SchemeWeaver.Composing;

/// <summary>
/// Marker for a composer that REPLACES the <see cref="Services.ISchemaAutoMapper"/>
/// registration outright (registers the interface again, last wins) rather than wrapping
/// what was there. The AI satellite's composer is one.
/// </summary>
/// <remarks>
/// <para>
/// Umbraco orders composers only where they say so. Two satellites that both declare
/// <c>[ComposeAfter(typeof(SchemeWeaverComposer))]</c> run in type-scan order, which is
/// unspecified, so a satellite that DECORATES the seam (the TypeSafe satellite) could compose
/// before a replacing one and be silently discarded, or after it and wrap it. Which one you
/// got depended on assembly load order.
/// </para>
/// <para>
/// <see cref="ComposeAfterAttribute"/> can target an interface: it then means "after every
/// enabled composer implementing it", and is weak by default, so it is satisfied when no such
/// composer exists. A decorating composer therefore declares
/// <c>[ComposeAfter(typeof(ISchemaAutoMapperReplacingComposer))]</c> and always runs last,
/// wrapping the replacement as its fallback (a cascade), with nothing to reference at
/// compile time and nothing to configure. A replacing composer only has to implement this
/// interface; it inherits <see cref="IComposer"/>, so it is a drop-in for the usual base.
/// </para>
/// </remarks>
public interface ISchemaAutoMapperReplacingComposer : IComposer
{
}
