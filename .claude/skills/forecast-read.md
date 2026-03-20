---
description: Read and summarise a Forecast ticket. Use when the user asks to look at, read, or check a Forecast task.
argument-hint: <ticket-url-or-id>
allowed-tools: mcp__claude-in-chrome__tabs_context_mcp, mcp__claude-in-chrome__tabs_create_mcp, mcp__claude-in-chrome__navigate, mcp__claude-in-chrome__computer, mcp__claude-in-chrome__find, mcp__claude-in-chrome__read_page, mcp__claude-in-chrome__get_page_text, mcp__claude-in-chrome__javascript_tool, ToolSearch
---

# Read a Forecast Ticket

Read and summarise the Forecast ticket at `$ARGUMENTS`.

## Steps

1. **Load browser tools**: Use `ToolSearch` to load any required `mcp__claude-in-chrome__*` tools before calling them.
2. **Get browser context**: Call `tabs_context_mcp` to check current tabs.
3. **Navigate to the ticket**: If the ticket is not already open, navigate to the URL. If given just a ticket ID like `T204941`, navigate to `https://app.forecast.it/T204941`.
4. **Wait** 2-3 seconds for the page to load.
5. **Read the page**: Use `get_page_text` to extract all text content from the ticket. This is more reliable than `read_page` for Forecast as the DOM is very large.
6. **Discover attachments**: Use `javascript_tool` to run the following script and find attached images and documents:

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

7. **View attachment images**: For each image discovered in step 6:
   - Navigate to the image's `src` URL in the current tab (same session preserves auth cookies)
   - Take a `screenshot` with the `computer` tool so you can see the image content
   - Navigate back to the ticket
   - If there are 5+ images, screenshot the thumbnail grid on the ticket page instead of navigating to each individually
   - For documents (PDFs, etc.), just record the filename and download URL — don't navigate to them
   - If step 6 returns empty results, scroll down on the ticket page and re-run the script (attachments may be lazy-loaded). Also try using `find` with queries like "attachment" or "file" and screenshot any results.

8. **Summarise** the ticket with the following structure:

```
## Ticket Summary

- **ID**: T######
- **URL**: https://app.forecast.it/T######
- **Project**: P### - Name
- **Phase**: ...
- **Sprint**: ...
- **Status**: ...
- **Priority**: ...
- **Assignee(s)**: ...
- **Start**: ...
- **Deadline**: ...

### Description
(Client details and bug/feature description)

### Comments
(Each comment with author, date, and content)

### Attachments
- **Images** (N attached):
  - image1.png — [visual description of what the image shows]
  - image2.jpg — [visual description]
- **Documents** (N attached):
  - report.pdf — [download URL]
```

## Forecast UX Notes

- Forecast pages are very large DOM trees. Always prefer `get_page_text` over `read_page` to avoid the 50000 character limit.
- If `read_page` is needed for interactive elements, use `depth: 2` or `filter: interactive`.

## Forecast Attachment UX Notes

- Attachments may appear in a dedicated "Files" section, inline in the description, or within comments.
- If the discovery script returns empty results, scroll down and re-run — attachments may be lazy-loaded.
- Image URLs use the same session cookie, so navigating to them in the same tab preserves authentication.
- Fallback: use `find` with "attachment" or "file" queries, then take a screenshot of the results.
