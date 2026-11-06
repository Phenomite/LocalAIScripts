---
name: web-browser-fetch
description: When read fails, fetch blocked or dynamic websites through the user browser session, with RSS/API fallbacks.
---

# Web Browser Fetch

1. Try sanctioned static paths first: RSS/Atom feeds, public JSON APIs, search-engine snippets, and archives.
2. If those fail, attach to the running browser with Eval's `browser` prelude.

## Attach and fetch

```js
const tab = await browser.open({ url, headed: true, wait_until: "networkidle2", timeout: 60 });
const text = await tab.text();
await tab.close();
```

`browser.open({ headed: true })` uses the configured relay when `browser.relay` is on, attaching to the user's running Chrome; otherwise it falls through to configured CDP, Tern, cmux, or managed Chromium. `app: { relay: true }` is the explicit form. `browser.targets({ app: { relay: true } })` lists the user's pages, and `app.target` adopts one.

`tab.text()` reads the page body. `tab.extract("markdown")` often returns only a fragment, so prefer `tab.text()`. `wait_until: "networkidle2"` is the reliable default for JS-rendered pages; `wait_until: "load"` is not enough. If a page keeps network activity, use `tab.waitForText`/`tab.waitForSelector` instead. `tab.url` and `tab.title` are methods: `await tab.url()`, `await tab.title()`.

For long pages, save `tab.text()` to `%TEMP%` and read ranges; `read` truncates long lines and outlines files over 150 lines. Use `tab.observe()` and `tab.run()` for DOM/ARIA inspection and JavaScript. Verify the actual page after every interaction. For screenshots, save locally and read with `read`.

Relay sessions are the user's real logged-in browser; never navigate a borrowed tab without authorization.

## Reddit

Reddit often blocks static fetch. Try the post's `.rss` feed:

```js
read("https://www.reddit.com/r/rust/comments/1xog337/wrong_ways_to_create_strings/.rss")
```

If RSS is unavailable, use the headed browser session. `old.reddit.com` may need login. Download preview images with `curl` and read them locally.
