---
description: Review code for bugs, security issues, and improvements
---

Review the following code or changes for:

1. **Bugs** — logic errors, off-by-one mistakes, null/undefined risks, race conditions
2. **Security** — injection, XSS, auth issues, exposed secrets, OWASP top 10
3. **Performance** — unnecessary allocations, N+1 queries, missing indexes, blocking calls
4. **Readability** — unclear naming, missing context, overly clever code

For each issue found, state:
- **Severity** (critical / warning / suggestion)
- **Location** (file and line)
- **Problem** and **fix**

If no issues are found in a category, skip it. Be concise — don't pad the review with praise.

$ARGUMENTS
