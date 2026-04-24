---
description: "Use the DotCraft Desktop embedded browser to inspect and operate local web UIs."
tools: BrowserJs
---

# Browser Use

Use this skill when you need to inspect, navigate, test, or automate a local browser target such as `localhost`, `127.0.0.1`, `::1`, `file://`, or `dotcraft-viewer:`.

Call `BrowserJs` with JavaScript. The runtime exposes:

- `agent.browser.nameSession(name)`
- `agent.browser.tabs.list()`
- `agent.browser.tabs.new(url?)`
- `agent.browser.tabs.selected()`
- `agent.browser.tabs.get(id)`
- `display(imageLike)`

Tab objects support:

- `tab.navigate(url)`
- `tab.url()`
- `tab.title()`
- `tab.domSnapshot()`
- `tab.screenshot()`
- `tab.evaluate(expressionOrFunction)`
- `tab.click(selector)`
- `tab.type(selector, text)`
- `tab.press(selector, key)`
- `tab.waitForLoadState(state?, timeoutMs?)`
- `tab.consoleLogs()`

Example:

```js
await agent.browser.nameSession("local app");
const tab = await agent.browser.tabs.new("http://localhost:3000");
await tab.waitForLoadState("load");
const snapshot = await tab.domSnapshot();
const image = await tab.screenshot();
await display(image);
return snapshot;
```

The browser automation runtime is intentionally local-first. Remote `http` and `https` origins that are not local addresses are blocked in this version.
