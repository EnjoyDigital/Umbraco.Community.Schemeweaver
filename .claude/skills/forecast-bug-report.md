---
description: Read a Forecast bug ticket and create a structured bug report markdown file. Use when the user asks to create a bug report from a Forecast task.
argument-hint: <ticket-url-or-id>
allowed-tools: mcp__claude-in-chrome__tabs_context_mcp, mcp__claude-in-chrome__tabs_create_mcp, mcp__claude-in-chrome__navigate, mcp__claude-in-chrome__computer, mcp__claude-in-chrome__find, mcp__claude-in-chrome__read_page, mcp__claude-in-chrome__get_page_text, mcp__claude-in-chrome__javascript_tool, ToolSearch, Read, Write, Glob, Grep, Bash, Agent
---

# Create a Bug Report from a Forecast Ticket

Read the Forecast ticket at `$ARGUMENTS`, investigate the codebase for root cause, and produce a structured bug report.

## Steps

### 1. Read the Forecast Ticket

Follow the same approach as the `forecast-read` skill:
- Load browser tools via `ToolSearch`
- Call `tabs_context_mcp`, navigate to the ticket
- Use `get_page_text` to extract all content
- Capture: ticket ID, project, sprint, status, priority, assignee, dates, client details, description, all comments, and any linked resources (Loom videos, etc.)
- Run the attachment discovery script (same as forecast-read) via `javascript_tool` to find attached images and documents:

```javascript
(() => {
  const results = { images: [], files: [] };
  const panel = document.querySelector('[class*="taskDetail"], [class*="TaskDetail"], [class*="task-detail"], [role="dialog"], [class*="sidePanel"], [class*="drawer"]') || document.body;
  panel.querySelectorAll('img').forEach(img => {
    const w = img.naturalWidth || img.width || 0;
    const h = img.naturalHeight || img.height || 0;
    if (w > 40 || h > 40 || img.src.includes('attachment') || img.src.includes('upload')) {
      results.images.push({ src: img.src, alt: img.alt, width: w, height: h });
    }
  });
  panel.querySelectorAll('[style*="background-image"]').forEach(el => {
    const match = el.getAttribute('style')?.match(/url\(["']?([^"')]+)["']?\)/);
    if (match) results.images.push({ src: match[1], alt: 'thumbnail', width: el.offsetWidth, height: el.offsetHeight });
  });
  panel.querySelectorAll('a[href]').forEach(a => {
    const href = a.href || '';
    if (href.match(/\.(pdf|doc|docx|xls|xlsx|png|jpg|jpeg|gif|svg|zip|csv)(\?|$)/i) || href.includes('download') || href.includes('attachment') || a.hasAttribute('download')) {
      results.files.push({ href, text: a.textContent?.trim()?.substring(0, 200), download: a.getAttribute('download') });
    }
  });
  return JSON.stringify(results, null, 2);
})()
```

- For each attached image, navigate to its URL in the current tab and take a `screenshot` with the `computer` tool so you can describe what it shows (e.g. the bug's visual symptoms), then navigate back
- For documents, record filenames and download URLs

### 2. Investigate the Codebase

- Search the codebase for code related to the bug
- Read relevant source files
- Identify the root cause with specific file paths and line numbers
- Use `Agent` with `subagent_type: Explore` for broader searches if needed

### 3. Create the Bug Report

Write a markdown file to `docs/bugs/<ticket-id>-<slug>.md` using this template:

```markdown
# <Ticket ID> - <Ticket Title>

**Forecast**: [<Ticket ID>](https://app.forecast.it/<Ticket ID>)
**Project**: <Project ID> - <Project Name>
**Sprint**: <Sprint Name>
**Status**: <Status>
**Priority**: <Priority>
**Assignee**: <Name>
**Created**: <Date>
**Start**: <Date>
**Deadline**: <Date>

---

## Client Details

| Field | Value |
|-------|-------|
| Company | <Company> |
| Client Name | <Name> |
| Client Email | <Email> |

## Bug Description

<Description from ticket>

### Reproduction Steps

1. <Step 1>
2. <Step 2>
3. ...

## Summary

<Plain English explanation of the bug for PMs and clients. Describe: what's broken, what causes it (non-technical), what the fix is (non-technical), and the expected impact. Avoid code references, method names, or technical jargon. This section should be understandable by anyone on the project.>

## Comments from Forecast

**<Author>** (<Date>):
> <Comment text>

### Linked Resources

- <Loom videos, screenshots, etc.>

## Attachments

| # | Filename | Description |
|---|----------|-------------|
| 1 | image1.png | [What the screenshot shows] |
| 2 | image2.jpg | [What the screenshot shows] |

## Technical Root Cause

<Detailed technical explanation of what causes the bug, referencing specific code. This section is for developers — include code references, method names, data flow, and root cause analysis.>

### Code References

**`<file path:line range>`**

(code snippet)

### Data Flow

(Visual flow diagram showing where the bug occurs)

## Suggested Fix

<Code change with explanation>

## Impact

- **Affected**: <What is affected>
- **Not affected**: <What still works>
- **Severity**: <Low/Medium/High>
```

### 4. Verify

- Check the file was created in `docs/bugs/`
- Read it back to confirm it's well-structured and accurate

## Notes

- **Two-tier format**: Bug reports must have a plain English "Summary" section (after Bug Description) for PMs/clients, and a separate "Technical Root Cause" section for developers. The Summary should be understandable by anyone; the Technical Root Cause contains code references and implementation details.
- Always use British English spelling conventions
- Create the `docs/bugs/` directory if it doesn't exist
- Use kebab-case for the filename slug (e.g. `T204941-ampersand-not-showing-in-product-names.md`)
- Include actual code snippets from the codebase in the root cause section
- Reference specific file paths and line numbers
- Attached screenshots should inform the Bug Description and Reproduction Steps — if an image shows a visual bug, describe what's visible in plain English
