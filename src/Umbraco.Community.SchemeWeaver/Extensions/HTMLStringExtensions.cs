using System.Text.RegularExpressions;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.Extensions
{
    internal static partial class HtmlStringExtensions
    {
        internal static IHtmlEncodedString? RemoveOuterTags(this IHtmlEncodedString? html)
        {
            if (html.IsNullOrWhiteSpace(true))
            {
                return default;
            }

            var headingContent = html.ToHtmlString()!;

            // Find the positions of the first and last angle brackets
            int startIndex = headingContent.IndexOf('>') + 1;
            int endIndex = headingContent.LastIndexOf('<');

            string? newString;
            // Extract the inner content between the outer tags
            if (startIndex >= 0 && endIndex > startIndex)
            {
                newString = headingContent[startIndex..endIndex];
            }
            else
            {
                newString = headingContent;
            }
            newString = StartParagraphRegex().Replace(newString, string.Empty);
            newString = EndParagraphRegex().Replace(newString, string.Empty);
            return new HtmlEncodedString(newString);
        }

        [GeneratedRegex("<p>", RegexOptions.Compiled | RegexOptions.Singleline)]
        private static partial Regex StartParagraphRegex();

        [GeneratedRegex("</p>", RegexOptions.Compiled | RegexOptions.Singleline)]
        private static partial Regex EndParagraphRegex();
    }
}
