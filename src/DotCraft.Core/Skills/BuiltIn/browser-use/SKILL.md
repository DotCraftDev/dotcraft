---
description: "Use the DotCraft Desktop embedded browser to inspect and operate local or approved web targets."
tools: BrowserJs
---

# Browser Use

Use this skill when you need to inspect, navigate, test, or automate a browser target in DotCraft Desktop.

Prefer this skill for local development and workspace targets such as `localhost`, `127.0.0.1`, `::1`, `file://`, or `dotcraft-viewer:`.

External `http` and `https` URLs can also be opened when the Desktop Browser Use settings allow them. External navigation may ask the user for approval, allowed domains may open without asking, and blocked domains will fail.

Call `BrowserJs` with JavaScript. The runtime exposes:

- `agent.browser.nameSession(name)`
- `agent.browser.tabs.list()`
- `agent.browser.tabs.new(url?)`
- `agent.browser.tabs.selected()`
- `agent.browser.tabs.get(id)`
- `display(imageLike)`

Tab objects support:

- `tab.navigate(url)` / `tab.goto(url)`
- `tab.back()` / `tab.forward()` / `tab.reload()` / `tab.close()`
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
- `tab.playwright.domSnapshot()`
- `tab.playwright.screenshot(options?)`
- `tab.playwright.locator(selector)`
- `tab.playwright.getByRole(role, { name?, exact? })`
- `tab.playwright.getByText(text, { exact? })`
- `tab.playwright.getByLabel(text, { exact? })`
- `tab.playwright.getByPlaceholder(text, { exact? })`
- `tab.playwright.getByTestId(testId)`
- `tab.cua.move({ x, y })`
- `tab.cua.click({ x, y })`
- `tab.cua.double_click({ x, y })`
- `tab.cua.drag({ path })`
- `tab.cua.scroll({ x, y, scrollX, scrollY })`
- `tab.cua.type({ text })`
- `tab.cua.keypress({ keys })`
- `tab.cua.get_visible_screenshot()`
- `tab.dev.logs({ filter?, levels?, limit? })`

Playwright-like locators support `count()`, `click()`, `dblclick()`, `fill(value)`, `type(value)`, `press(key)`, `innerText()`, `textContent()`, `getAttribute(name)`, `isVisible()`, `isEnabled()`, and `waitFor({ state?, timeoutMs? })`. Locator actions are strict: zero matches or multiple matches fail instead of guessing. Locator clicks and coordinate CUA actions render a virtual cursor in the Desktop browser tab when the page accepts the overlay.

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

If navigation is denied or blocked, explain that Browser Use access is controlled by Desktop settings and ask the user to approve the request or update the allowed/blocked domain lists. Do not try to bypass the Desktop Browser Use policy.
