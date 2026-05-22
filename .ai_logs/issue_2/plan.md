1. **Analyze Content Script `performStealthInjection`**: 
   The current logic attempts to submit the form directly (`passField.form.submit()`) if it doesn't find an input with `type="submit"`. 
   For Trend Micro OfficeScan and similar applications, forms often use a `<button type="button">` connected to a JavaScript function.
   Calling `form.submit()` bypasses the JavaScript `login()` function, triggering a default form submission which leads to the page reloading without actually authenticating.

2. **Adjust Auto-Submit Logic in `Content.js`**:
   - Move the `passField.form.submit()` fallback to the very end of the auto-submit chain (Versuch 4).
   - Enhance the button heuristic (Versuch 3/new Versuch 2) to search for keywords (`login`, `sign in`, `signin`, `anmelden`, `einloggen`, `weiter`, `next`) not only in `innerText` and `value`, but also in the element's `id`, `name`, and `className`.
   - In Trend Micro's case, the button has `id="btn-signin"` but no text until populated by `l10n`. Including `id` and adding `"signin"` to the search terms will catch it.
   - Click the button rather than raw-submitting the form.