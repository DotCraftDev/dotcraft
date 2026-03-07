---
description: "Browser automation via Playwright MCP - navigate, click, fill forms, take screenshots, and inspect web pages."
bins: npx
---

# Browser Automation (Playwright MCP)

You have access to browser automation tools provided by the Playwright MCP server.
These tools let you control a real browser to interact with web pages.

## Setup

> **If no `browser_*` tools are available**, the Playwright MCP server is not configured.
> Tell the user to add the following to their `config.json` and restart DotCraft:

This skill requires the Playwright MCP server to be configured in `config.json`:

```json
{
  "McpServers": [
    {
      "Name": "playwright",
      "Transport": "stdio",
      "Command": "npx",
      "Arguments": ["-y", "@playwright/mcp@latest"]
    }
  ]
}
```  

Prerequisites:
- Node.js 18+
- Chrome browser: `npx playwright install chrome`

### If Browser Installation Fails

If `browser_install` fails (e.g. due to permission errors or network restrictions), tell the user to install Chrome manually with administrator privileges:

1. Open **PowerShell as Administrator** (right-click the Start menu → "Windows PowerShell (Admin)" or "Terminal (Admin)")
2. Run:
   ```powershell
   npx playwright install chrome
   ```
3. Wait for the download to complete, then retry the task.

### If the Browser Is Not Supported on the User's OS

Playwright supports multiple browsers: **Chrome**, **Firefox**, and **WebKit**. If one browser fails to install or run on the user's OS (e.g. WebKit is not supported on Windows), try a different one.

**Step 1 — Install an alternative browser:**

```powershell
# Try Firefox
npx playwright install firefox

# Or try WebKit (macOS / Linux only)
npx playwright install webkit
```

**Step 2 — Update `config.json` to specify the browser** using the `--browser` argument:

```json
{
  "McpServers": [
    {
      "Name": "playwright",
      "Transport": "stdio",
      "Command": "npx",
      "Arguments": ["-y", "@playwright/mcp@latest", "--browser", "firefox"]
    }
  ]
}
```

Supported values for `--browser`: `firefox`, `webkit`, `chrome`, `msedge`.

> **Tip:** On Linux servers, stick to `chrome` or `firefox` (headless). WebKit is only reliable on macOS.

## Secrets (Credentials Management)

The Playwright MCP server supports a `--secrets` parameter that loads a dotenv file containing sensitive credentials. When secrets are configured, you **MUST** use the secret key names (not the actual values) when filling in sensitive form fields like passwords.

**How it works:**
- The `--secrets` flag points to a `.env` file with key-value pairs (e.g. `MY_PASSWORD=actual_password_value`)
- When you call `browser_type` or `browser_fill_form`, pass the **key name** (e.g. `MY_PASSWORD`) as the `text`/`value` parameter instead of the actual password
- The Playwright MCP server will automatically replace the key name with the real value before typing it into the browser
- In the response, any occurrence of the real secret value will be redacted as `<secret>KEY_NAME</secret>`

**Example:** If the secrets file contains `LOGIN_PASSWORD=abc123`, then to fill a password field:
- Use `browser_type` with `text: "LOGIN_PASSWORD"` — the server will type `abc123` into the browser
- Do NOT type the actual password value — always use the key name

**Important:** The secret key names are the identifiers defined in the `.env` file. You should ask the user what secret keys are available if you encounter a login page and don't know the key names.