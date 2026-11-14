---
description: "Browser tool: clear broken OMP UA per tab, drive the user's browser safely, and Site gotchas"
alwaysApply: true
---
# Control the Browser

OMP sets UA that empties `navigator.userAgentData` while keeping `navigator.userAgent`. Clear it on every tab after `browser.open` and before the first navigation:

```js
await t.run(async ({ page }) => {
  const cdp = await page.target().createCDPSession();
  await cdp.send("Network.setUserAgentOverride", { userAgent: "", acceptLanguage: "", platform: "", userAgentMetadata: null });
  await cdp.detach();
});
```

If `navigator.userAgentData.getHighEntropyValues(["fullVersionList"])` returns empty, the UA override is still set.

## Using browser

- The relay uses the user's logged-in browser. On tabs borrowed with `target:`, never navigate or clear cookies/storage without permission.
- Release tabs with `browser.close({ all: true })` (borrowed tabs are released, not closed).
- Keep request rates human.

## Site Gotchas

- eBay: `/sch/i.html?_nkw=<q>&LH_Sold=1&LH_Complete=1&_ipg=200`. Filter titles so unrelated items don't skew costs.
- Facebook Marketplace: search `input[aria-label="Search Marketplace"]`, URL `/marketplace/<nodeId>/search/?query=<term>` (`?q=` returns the feed). Wait 3s and scroll once.
- Reddit: use `old.reddit` `#header-bottom-right .user a` (`me.json` and the `www` header omit username).

## Browser Gotchas

- `browser.open({ url, name })`; `tab.press("Enter", { selector })`; Use `aria/Name`, CSS, or XPath selectors (never `has-text()`).
- `tab.evaluate` takes an expression or IIFE (no top-level `return`). JS eval cells print with `print()`.
- `"Chrome detached omp from this tab"` (debugging bar cancelled): reuse an adopted tab or set `app: "auto"`.
- `"Attempted to use detached Frame"` (idle tab closed): re-open it (`persist: true` survives turns).
