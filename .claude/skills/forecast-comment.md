---
description: Add a new comment to a Forecast ticket. Use when the user asks to post or add a comment on a Forecast task.
argument-hint: <ticket-url-or-id>
allowed-tools: mcp__claude-in-chrome__tabs_context_mcp, mcp__claude-in-chrome__tabs_create_mcp, mcp__claude-in-chrome__navigate, mcp__claude-in-chrome__computer, mcp__claude-in-chrome__find, mcp__claude-in-chrome__read_page, mcp__claude-in-chrome__get_page_text, mcp__claude-in-chrome__form_input, mcp__claude-in-chrome__javascript_tool, ToolSearch
---

# Add a Comment to a Forecast Ticket

Post a new comment on the Forecast ticket at `$ARGUMENTS`.

## Steps

1. **Load browser tools**: Use `ToolSearch` to load any required `mcp__claude-in-chrome__*` tools before calling them.
2. **Get browser context**: Call `tabs_context_mcp` to check current tabs.
3. **Navigate to the ticket**: If not already open, navigate to the URL.
4. **Wait** 2-3 seconds for the page to load.
5. **Find the comment input**: Use `find` tool to search for `Comment input field`. This returns a ref to the comment combobox.
6. **Click the comment input** using the ref from the find tool. This activates the editor and shows the formatting toolbar.
7. **Type the comment** using the `computer` tool with action `type`.
8. **Submit the comment** by pressing `Enter` or finding and clicking the send/submit button. The submit button appears as a green arrow icon at the bottom-right of the comment editor after text has been entered. Use `find` tool to locate `submit button` or `send button`.

## IMPORTANT: Always Submit

After typing a comment, ALWAYS immediately submit it. Never ask the user for confirmation before submitting.

## Comment Formatting

Forecast comments are read by project managers and clients, not just developers. Always write comments in a two-tier format:

1. **Top section (PM/client-friendly)**: Lead with a plain English explanation. Describe what's broken, what caused it (non-technical), what the fix is (non-technical), and any next steps or timeline. Avoid code references, method names, or technical jargon in this section.

2. **Bottom section (Technical details)**: If there are technical details worth sharing, add them below a clear separator line like `--- Technical Details ---`. Include code references, method names, data flow, and root cause analysis here. This section is optional — only include it when there are meaningful technical details to share.

Example format:
```
The product names with special characters (like &) are displaying incorrectly on the website. This is caused by the system encoding the characters for HTML but not decoding them back when preparing the display name.

Fix: one-line change to decode the characters after HTML processing. Will be included in the next deployment.

--- Technical Details ---

GetProductDisplayName() calls StripHtml() which removes HTML tags but doesn't decode entities like &amp;. Adding HtmlDecode() after StripHtml() resolves the issue.
```

## Forecast Comment UX Notes

- The comment input is a combobox at the bottom of the ticket panel with placeholder text "Comment".
- Clicking it reveals a rich text editor with formatting toolbar (bold, italic, underline, lists, etc.).
- The comment input must be clicked via its element ref (use `find` tool) - coordinate clicks on the placeholder text are unreliable.
- After typing, the submit button (green arrow) appears at the bottom-right of the editor.
- `@mentions` can be typed directly (e.g. `@Robbie Smith`) and Forecast will show a suggestion dropdown.
