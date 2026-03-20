---
description: Update the workflow status of a Forecast ticket. Use when the user asks to change a ticket status (e.g. To-do, In Progress, Done).
argument-hint: <ticket-url-or-id> <new-status>
allowed-tools: mcp__claude-in-chrome__tabs_context_mcp, mcp__claude-in-chrome__tabs_create_mcp, mcp__claude-in-chrome__navigate, mcp__claude-in-chrome__computer, mcp__claude-in-chrome__find, mcp__claude-in-chrome__read_page, mcp__claude-in-chrome__get_page_text, mcp__claude-in-chrome__javascript_tool, ToolSearch
---

# Update Forecast Ticket Status

Change the workflow status of a Forecast ticket.

**Ticket**: `$0`
**New Status**: `$1`

## Valid Statuses

The Forecast workflow statuses in order are:
- New
- To-do
- Estimating
- Ready for Dev
- In Progress
- To (Dev) Deploy
- UAT
- To (Test) Deploy
- Deployed
- Done

## Steps

1. **Load browser tools**: Use `ToolSearch` to load any required `mcp__claude-in-chrome__*` tools before calling them.
2. **Get browser context**: Call `tabs_context_mcp` to check current tabs.
3. **Navigate to the ticket**: If the ticket is not already open, navigate to the URL. If given just a ticket ID like `T204941`, navigate to `https://app.forecast.it/T204941`.
4. **Wait** 2-3 seconds for the page to load.
5. **Take a screenshot** to confirm the ticket is loaded and find the current status.
6. **Click the Workflow Status** field in the top-right area of the ticket panel. It shows the current status text (e.g. "To-do", "In Progress").
7. **Take a screenshot** to confirm the dropdown appeared with the list of statuses.
8. **Click the desired status** from the dropdown list.
9. **Take a screenshot** to verify the status has changed.

## Forecast UX Notes

- The status dropdown is in the top-right of the ticket detail panel, labelled "WORKFLOW STATUS".
- Clicking the status text opens a dropdown with all available statuses.
- The change is saved immediately when a status is selected - no save button needed.
