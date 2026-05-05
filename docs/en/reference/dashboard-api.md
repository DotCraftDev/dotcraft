# DotCraft Dashboard API

Dashboard API is intended for the debugging UI and internal tools. Most users should use the Dashboard pages directly; use this page when building integrations or debugging the frontend.

## Trace Event Types

| Type | Description |
|------|-------------|
| `session.started` | Session started |
| `session.completed` | Session completed |
| `turn.started` | Agent turn started |
| `turn.completed` | Agent turn completed |
| `tool.started` | Tool call started |
| `tool.completed` | Tool call completed |
| `tool.failed` | Tool call failed |
| `approval.requested` | Human approval requested |
| `approval.completed` | Approval completed |
| `error` | Runtime error |

Dashboard `Thinking` and `Response` trace events are recorded by contiguous streaming content segment. They are not recorded per chunk, and a full turn is not forced into a single event. `ThinkingCount` and `ResponseCount` therefore represent segment counts. The realtime event stream sends a segment event after that segment ends and is recorded; historical traces are not migrated, so older data may still use the previous granularity.

## Endpoints

### `GET /DashBoard`

Returns the Dashboard page.

### `GET /DashBoard/api/summary`

Returns runtime summary, including session count, recent events, and module state.

### `GET /DashBoard/api/sessions`

Returns sessions visible to Dashboard.

### `GET /DashBoard/api/sessions/{sessionKey}/events`

Returns trace events for one session.

### `GET /dashboard/api/orchestrators/automations/state`

Returns Automations orchestrator state, including tasks, Cron, and GitHub source summaries.

### `POST /dashboard/api/orchestrators/automations/refresh`

Requests an Automations state refresh.

### `GET /dashboard/api/config/schema`

Returns the configuration schema used by the Dashboard Settings page.

### `DELETE /api/sessions/{sessionKey}`

Deletes one Dashboard session record.

### `DELETE /api/sessions`

Clears Dashboard session records.

### `GET /api/events/stream`

Returns the event stream used by Dashboard.

## Usage Notes

- API path casing follows the existing Dashboard routes.
- Prefer binding to `127.0.0.1` for local debugging.
- Do not expose an unprotected Dashboard in production or shared networks.
