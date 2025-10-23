Please refer to [guidelines.md](./guidelines.md) for coding conventions and AI assistant behavior.

## Manual testing

### Taste profile generator streaming

1. Sign in to Wine Surfer with a user that has scored bottles, then browse to `/wine-surfer/taste-profile`.
2. Click **Generate profile** and verify that the status banner updates while streaming (e.g., "Contacting the taste profile assistant…"), the summary textarea fills incrementally, and the profile textarea updates as new text arrives.
3. Confirm that, once generation completes, the banner switches to the success message and the suggested appellations list refreshes.
4. To simulate an environment without streaming support, open the browser console and run `window.__forceTasteProfileTextFallback = true;` before clicking **Generate profile**. The UI should wait for the full payload and still populate the summary, profile, and suggestions when the request finishes. Reset with `delete window.__forceTasteProfileTextFallback;` afterwards.
