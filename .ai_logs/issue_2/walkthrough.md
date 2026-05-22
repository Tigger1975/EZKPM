The plan addresses the failure of the browser extension to auto-submit correctly on specific login forms (e.g. Trend Micro OfficeScan).

1. **Adjust Auto-Submit Logic in Content Script:**
   Forms in modern SPAs or enterprise products often use elements like `<button type="button">` connected to a JS `login()` handler, preventing the form's native `.submit()` from correctly authorizing the user.
2. **Prioritize Heuristic Button Search:**
   Relocating the heuristic button search ahead of the hard `form.submit()` fallback.
3. **Broaden Keyword Search:**
   Enhance the button detection to scan `id`, `name`, `className`, and `rel` tags (e.g., catching `id="btn-signin"`). Adding missing keywords like `signin` to robustly catch buttons without inner text content before l10n is applied.
4. **Action:**
   Simulate a genuine `.click()` on the identified login button instead of immediately invoking `.submit()` on the form node, ensuring JavaScript bindings trigger successfully.