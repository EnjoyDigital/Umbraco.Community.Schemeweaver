---
description: Edit an existing comment on a Forecast ticket. Use when the user asks to update, edit, or change a comment on a Forecast task.
argument-hint: <ticket-url-or-id>
allowed-tools: mcp__claude-in-chrome__tabs_context_mcp, mcp__claude-in-chrome__tabs_create_mcp, mcp__claude-in-chrome__navigate, mcp__claude-in-chrome__computer, mcp__claude-in-chrome__find, mcp__claude-in-chrome__read_page, mcp__claude-in-chrome__get_page_text, mcp__claude-in-chrome__form_input, mcp__claude-in-chrome__javascript_tool, ToolSearch
---

# Edit an Existing Comment on a Forecast Ticket

Edit a comment on the Forecast ticket at `$ARGUMENTS`.

## Steps

1. **Load browser tools**: Use `ToolSearch` to load any required `mcp__claude-in-chrome__*` tools before calling them.
2. **Get browser context**: Call `tabs_context_mcp` to check current tabs.
3. **Navigate to the ticket**: If not already open, navigate to the URL.
4. **Wait** 2-3 seconds for the page to load.
5. **Scroll down** to find the Comments section. If the comments section is collapsed (shows "Comments (N)" with a chevron), click it to expand.
6. **Scroll to the target comment** - identify the correct comment by author and content.

### Opening the Edit Menu (CRITICAL - Forecast has tricky UX)

7. **Find the three-dot menu**: The comment has a three-dot menu (vertical ellipsis) on its right side. Use the `computer` tool to click at the approximate coordinates of the dots. The dots are a `div.new-ui-actions-menu` element positioned at the right edge of the comment.

   If the coordinate click does not open the menu, use this JavaScript approach:
   ```
   Find the menu element:
   document.querySelectorAll('.new-ui-actions-menu')
   ```
   Then use a real `computer` click at the element's coordinates (JS `.click()` does NOT reliably open this menu - you must use the `computer` tool to click).

8. **Take a screenshot** to confirm the dropdown with "Edit" and "Delete" options appeared.

9. **Click Edit using JavaScript**: The Edit option is a `<li>` element with `id="option_0"`. You MUST use `javascript_tool` to click it:
   ```javascript
   document.querySelector('#option_0').click()
   ```
   A regular coordinate click on "Edit" in the dropdown will appear to work visually but does NOT actually activate edit mode. The JS click on `#option_0` is required.

10. **Click the comment text** to ensure the editor is focused. After the JS click, click somewhere in the comment body text area to activate the cursor.

11. **Select all and delete**: Press `Ctrl+A` then `Backspace` to clear the existing text.

12. **Type the new comment** using the `computer` tool with action `type`.

13. **Scroll down if needed** to see the SAVE button.

14. **Click SAVE**: Use `find` tool to locate the "SAVE button", then click it using its ref. The SAVE button is a green button labelled "Save" at the bottom-right of the edit area. A CANCEL button sits next to it.

15. **Take a screenshot** to verify the comment has been saved (SAVE/CANCEL buttons should disappear).

## IMPORTANT: Always Save

After editing a comment, ALWAYS click the SAVE button immediately. Never ask the user for confirmation before saving.

## Comment Formatting

Forecast comments are read by project managers and clients, not just developers. Always write comments in a two-tier format:

1. **Top section (PM/client-friendly)**: Lead with a plain English explanation. Describe what's broken, what caused it (non-technical), what the fix is (non-technical), and any next steps or timeline. Avoid code references, method names, or technical jargon in this section.

2. **Bottom section (Technical details)**: If there are technical details worth sharing, add them below a clear separator line like `--- Technical Details ---`. Include code references, method names, data flow, and root cause analysis here. This section is optional — only include it when there are meaningful technical details to share.

## Forecast Edit Comment UX Quirks Summary

| Step | Method | Why |
|------|--------|-----|
| Open three-dot menu | `computer` tool click | JS `.click()` does not trigger the menu |
| Click "Edit" option | `javascript_tool`: `document.querySelector('#option_0').click()` | Coordinate click appears to work but doesn't activate edit mode |
| Click SAVE button | `find` tool to get ref, then `computer` click on ref | Coordinate clicks can miss the button |
