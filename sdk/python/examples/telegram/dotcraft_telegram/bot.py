"""
DotCraft Telegram Adapter.

Maps each Telegram chat to a DotCraft thread. Supports:
- Text messages → turn/start
- Slash commands via server command pipeline (`command/execute`)
- Agent replies streamed and sent as a single composed message
- Approval flow via Telegram inline keyboard buttons
- ext/channel/deliver mapped to bot.send_message()
- Typing indicator while the agent is processing
"""

from __future__ import annotations

import asyncio
import base64
import io
import logging
import os
import re
import sys

from telegram import (
    BotCommand,
    CallbackQuery,
    InlineKeyboardButton,
    InlineKeyboardMarkup,
    ReplyParameters,
    Update,
)
from telegram.error import Conflict as TelegramConflict
from telegram.ext import (
    Application,
    CallbackQueryHandler,
    ContextTypes,
    MessageHandler,
    filters,
)
from telegram.request import HTTPXRequest

# Add the sdk/python directory to the path so dotcraft_wire is importable
# when running the example directly without installing the package.
# __file__ is at sdk/python/examples/telegram/dotcraft_telegram/bot.py
# three levels up reaches sdk/python/
_SDK_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "..")
if _SDK_DIR not in sys.path:
    sys.path.insert(0, _SDK_DIR)

from dotcraft_wire import (
    DECISION_ACCEPT,
    DECISION_ACCEPT_ALWAYS,
    DECISION_ACCEPT_FOR_SESSION,
    DECISION_CANCEL,
    DECISION_DECLINE,
    ChannelAdapter,
    StdioTransport,
)

from .formatting import markdown_to_telegram_html, split_message

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Approval decision labels shown on inline keyboard
# ---------------------------------------------------------------------------

_APPROVAL_BUTTONS = [
    (DECISION_ACCEPT, "✅ Approve"),
    (DECISION_ACCEPT_FOR_SESSION, "✅ Approve (this session)"),
    (DECISION_DECLINE, "❌ Decline"),
    (DECISION_CANCEL, "🛑 Cancel turn"),
]


class TelegramAdapter(ChannelAdapter):
    """
    Telegram channel adapter for DotCraft.

    Each Telegram chat (private or group) maps to one DotCraft thread.
    The adapter communicates with DotCraft via stdio (subprocess mode by default).
    """

    BOT_COMMANDS = [
        BotCommand("new", "Start a new conversation"),
        BotCommand("help", "Show available commands"),
    ]

    def __init__(
        self,
        bot_token: str,
        proxy: str | None = None,
    ) -> None:
        super().__init__(
            transport=StdioTransport(),
            channel_name="telegram",
            client_name="dotcraft-telegram",
            client_version="0.1.0",
            # Opt out of notifications the adapter doesn't need
            opt_out_notifications=[
                "item/reasoning/delta",
                "subagent/progress",
                "item/usage/delta",
                "system/event",
                "plan/updated",
            ],
        )
        self._bot_token = bot_token
        self._proxy = proxy

        self._app: Application | None = None
        # track chat_id for each user for delivery: user_id -> chat_id
        self._chat_ids: dict[str, int] = {}
        # typing indicator tasks: str(chat_id) -> Task
        self._typing_tasks: dict[str, asyncio.Task] = {}
        # pending approval futures: callback_data key -> Future[str]
        self._pending_approvals: dict[str, asyncio.Future[str]] = {}

    # ------------------------------------------------------------------
    # ChannelAdapter abstract methods
    # ------------------------------------------------------------------

    async def on_deliver(self, target: str, content: str, metadata: dict) -> bool:
        """Deliver a message to a Telegram chat."""
        if not self._app:
            logger.warning("Telegram bot not running; cannot deliver to %s", target)
            return False
        chat_id = self._parse_target_chat_id(target)
        if chat_id is None:
            logger.error("Invalid delivery target: %s", target)
            return False

        # Stop typing indicator when delivering any message (including slash-command
        # replies, which bypass on_turn_completed).
        self._stop_typing(str(chat_id))

        await self._send_text(chat_id, content)
        return True

    def get_delivery_capabilities(self) -> dict | None:
        return {
            "structuredDelivery": True,
            "media": {
                "file": {
                    "supportsHostPath": True,
                    "supportsUrl": True,
                    "supportsBase64": True,
                    "supportsCaption": True,
                }
            },
        }

    def get_channel_tools(self) -> list[dict] | None:
        return [
            {
                "name": "telegramSendDocumentToCurrentChat",
                "description": "Create a text document and send it to the current Telegram chat.",
                "requiresChatContext": True,
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        "fileName": {"type": "string"},
                        "contentText": {"type": "string"},
                        "caption": {"type": "string"},
                    },
                    "required": ["fileName", "contentText"],
                },
            }
        ]

    async def on_send(self, target: str, message: dict, metadata: dict) -> dict:
        kind = str(message.get("kind", ""))
        if kind == "text":
            return await super().on_send(target, message, metadata)

        if kind != "file":
            return {
                "delivered": False,
                "errorCode": "UnsupportedDeliveryKind",
                "errorMessage": f"Telegram example does not implement structured '{kind}' delivery.",
            }

        chat_id = self._parse_target_chat_id(target)
        if chat_id is None:
            return {
                "delivered": False,
                "errorCode": "AdapterDeliveryFailed",
                "errorMessage": f"Invalid Telegram target '{target}'.",
            }

        source = message.get("source") or {}
        caption = str(message.get("caption", ""))
        file_name = str(message.get("fileName", "attachment"))
        try:
            remote_message = await self._send_document_from_message(
                chat_id=chat_id,
                message=message,
                source=source,
                file_name=file_name,
                caption=caption,
            )
            return {
                "delivered": True,
                "remoteMessageId": str(getattr(remote_message, "message_id", "")),
            }
        except Exception as exc:
            logger.error("Structured file delivery failed: %s", exc)
            return {
                "delivered": False,
                "errorCode": "AdapterDeliveryFailed",
                "errorMessage": str(exc),
            }

    async def on_tool_call(self, request: dict) -> dict:
        tool = str(request.get("tool", ""))
        if tool != "telegramSendDocumentToCurrentChat":
            return {
                "success": False,
                "errorCode": "UnsupportedTool",
                "errorMessage": f"Unknown tool '{tool}'.",
            }

        args = request.get("arguments") or {}
        context = request.get("context") or {}
        target = str(context.get("channelContext") or context.get("groupId") or "")
        chat_id = self._parse_target_chat_id(target)
        if chat_id is None:
            return {
                "success": False,
                "errorCode": "MissingChatContext",
                "errorMessage": "Current tool call does not contain a Telegram chat target.",
            }

        file_name = str(args.get("fileName") or "note.txt")
        content_text = str(args.get("contentText") or "")
        caption = str(args.get("caption") or "")
        buffer = io.BytesIO(content_text.encode("utf-8"))
        buffer.name = file_name

        try:
            message = await self._app.bot.send_document(
                chat_id=chat_id,
                document=buffer,
                caption=caption or None,
                filename=file_name,
            )
            return {
                "success": True,
                "contentItems": [
                    {
                        "type": "text",
                        "text": f"Sent {file_name} to the current Telegram chat.",
                    }
                ],
                "structuredResult": {
                    "delivered": True,
                    "fileName": file_name,
                    "messageId": str(message.message_id),
                },
            }
        except Exception as exc:
            logger.error("Tool document send failed: %s", exc)
            return {
                "success": False,
                "errorCode": "AdapterToolCallFailed",
                "errorMessage": str(exc),
            }

    async def on_approval_request(self, request: dict) -> str:
        """Present an inline keyboard approval prompt in the Telegram chat."""
        thread_id = request.get("threadId", "")
        approval_type = request.get("approvalType", "")
        operation = request.get("operation", "")
        reason = request.get("reason", "")
        request_id = request.get("requestId", "")

        # Find the chat_id associated with this thread
        # We derive it from _thread_map (identity_key contains user_id)
        chat_id = self._find_chat_id_for_thread(thread_id)
        if chat_id is None:
            logger.warning("Cannot find chat for thread %s; auto-declining", thread_id)
            return DECISION_DECLINE

        # Build inline keyboard
        callback_prefix = f"approval:{request_id}"
        keyboard = [
            [InlineKeyboardButton(label, callback_data=f"{callback_prefix}:{decision}")]
            for decision, label in _APPROVAL_BUTTONS
        ]
        markup = InlineKeyboardMarkup(keyboard)

        prompt = (
            f"⚠️ <b>Agent approval required</b>\n"
            f"Type: <code>{approval_type}</code>\n"
            f"Operation: <code>{operation}</code>\n"
            f"Reason: {reason}"
        )

        await self._app.bot.send_message(
            chat_id=chat_id,
            text=prompt,
            parse_mode="HTML",
            reply_markup=markup,
        )

        # Wait for the user to respond via callback query
        future: asyncio.Future[str] = asyncio.get_event_loop().create_future()
        self._pending_approvals[callback_prefix] = future
        try:
            decision = await asyncio.wait_for(future, timeout=120)
        except asyncio.TimeoutError:
            logger.warning("Approval timed out for request %s", request_id)
            self._pending_approvals.pop(callback_prefix, None)
            return DECISION_CANCEL

        return decision

    async def on_turn_completed(
        self,
        thread_id: str,
        turn_id: str,
        reply_text: str,
        channel_context: str,
    ) -> None:
        """Send the accumulated reply to the Telegram chat."""
        self._stop_typing(channel_context)
        if reply_text:
            try:
                chat_id = int(channel_context)
            except ValueError:
                logger.error("Invalid channel_context for delivery: %s", channel_context)
                return
            await self._send_text(chat_id, reply_text)

    async def on_turn_failed(self, thread_id: str, turn_id: str, error: str) -> None:
        logger.error("Turn %s failed: %s", turn_id, error)

    async def on_turn_cancelled(self, thread_id: str, turn_id: str) -> None:
        logger.info("Turn %s cancelled", turn_id)

    # ------------------------------------------------------------------
    # Telegram bot setup and event handlers
    # ------------------------------------------------------------------

    async def run(self) -> None:
        """Start both the DotCraft connection and the Telegram long-polling loop."""
        # Start DotCraft connection in background
        await self.start()

        # Build and configure the Telegram application
        req = HTTPXRequest(
            connection_pool_size=16,
            pool_timeout=5.0,
            connect_timeout=30.0,
            read_timeout=30.0,
        )
        builder = Application.builder().token(self._bot_token).request(req).get_updates_request(req)
        if self._proxy:
            builder = builder.proxy(self._proxy).get_updates_proxy(self._proxy)
        self._app = builder.build()

        self._app.add_error_handler(self._on_error)
        self._app.add_handler(
            MessageHandler(filters.TEXT, self._on_message)
        )
        self._app.add_handler(CallbackQueryHandler(self._on_callback_query))

        # Initialize and start polling
        await self._app.initialize()
        await self._app.start()

        bot_info = await self._app.bot.get_me()
        logger.info("Telegram bot @%s connected", bot_info.username)

        try:
            bot_commands = await self._load_registered_commands()
            await self._app.bot.set_my_commands(bot_commands)
        except Exception as e:
            logger.warning("Failed to register bot commands: %s", e)

        # Wait for any previous getUpdates long-poll session to expire before
        # starting the new one. Telegram allows only one active getUpdates session
        # per bot token. We probe with timeout=0 (short-poll, returns immediately)
        # and retry for up to 65 seconds — the maximum Telegram long-poll timeout.
        await self._app.bot.delete_webhook(drop_pending_updates=True)
        _MAX_WAIT_SECS = 65
        _RETRY_INTERVAL = 5
        for _attempt in range(_MAX_WAIT_SECS // _RETRY_INTERVAL + 1):
            try:
                await self._app.bot.get_updates(offset=-1, timeout=0)
                break  # session is now available
            except TelegramConflict:
                if _attempt * _RETRY_INTERVAL >= _MAX_WAIT_SECS:
                    logger.error(
                        "Previous polling session did not expire after %ds. "
                        "Ensure no other bot process is running with this token. "
                        "Proceeding anyway; 409 errors may occur.",
                        _MAX_WAIT_SECS,
                    )
                    break
                logger.warning(
                    "Another bot process is actively polling (409 Conflict). "
                    "Waiting %ds before retry (%d/%d)...",
                    _RETRY_INTERVAL,
                    _attempt + 1,
                    _MAX_WAIT_SECS // _RETRY_INTERVAL,
                )
                await asyncio.sleep(_RETRY_INTERVAL)

        await self._app.updater.start_polling(
            allowed_updates=["message", "callback_query"],
            drop_pending_updates=True,
        )

        # Keep running until externally stopped
        while self._running:
            await asyncio.sleep(1)

        await self._shutdown_bot()

    async def _load_registered_commands(self) -> list[BotCommand]:
        """Build Telegram bot command list from server command metadata."""
        try:
            commands = await self._client.command_list()
        except Exception as e:
            logger.warning("Failed to load command/list from server: %s", e)
            return self.BOT_COMMANDS

        bot_commands: list[BotCommand] = []
        for item in commands:
            name = str(item.get("name") or "").strip().lstrip("/").lower()
            if not name:
                continue
            if len(name) > 32 or re.fullmatch(r"[a-z0-9_]+", name) is None:
                logger.debug("Skipping unsupported Telegram command name: %s", name)
                continue

            description = str(item.get("description") or "DotCraft command").strip()
            if not description:
                description = "DotCraft command"
            bot_commands.append(BotCommand(name, description[:256]))

        return bot_commands if bot_commands else self.BOT_COMMANDS

    async def _shutdown_bot(self) -> None:
        """Stop the Telegram polling loop and clean up."""
        for chat_id in list(self._typing_tasks):
            self._stop_typing(chat_id)
        if self._app:
            await self._app.updater.stop()
            await self._app.stop()
            await self._app.shutdown()
        await self.stop()

    # ------------------------------------------------------------------
    # Command / message handlers
    # ------------------------------------------------------------------

    async def _on_message(self, update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
        """Handle incoming text messages."""
        if not update.message or not update.effective_user:
            return
        user = update.effective_user
        message = update.message
        chat_id = message.chat_id

        self._chat_ids[str(user.id)] = chat_id
        self._start_typing(str(chat_id))

        await self.handle_message(
            user_id=str(user.id),
            user_name=user.full_name or user.username or str(user.id),
            text=message.text or "",
            channel_context=str(chat_id),
            sender_extra={"senderRole": "admin"},
        )

    async def _on_callback_query(
        self, update: Update, context: ContextTypes.DEFAULT_TYPE
    ) -> None:
        """Handle inline keyboard button presses (approval responses)."""
        query: CallbackQuery | None = update.callback_query
        if query is None or not query.data:
            return
        await query.answer()

        # callback_data format: "approval:<requestId>:<decision>"
        parts = query.data.split(":", 2)
        if len(parts) != 3 or parts[0] != "approval":
            return

        prefix = f"{parts[0]}:{parts[1]}"
        decision = parts[2]

        future = self._pending_approvals.pop(prefix, None)
        if future is not None and not future.done():
            future.set_result(decision)

        # Update the message to show the chosen action
        labels = {d: lbl for d, lbl in _APPROVAL_BUTTONS}
        chosen_label = labels.get(decision, decision)
        try:
            await query.edit_message_reply_markup(reply_markup=None)
            await query.edit_message_text(
                text=f"{query.message.text}\n\n<i>Decision: {chosen_label}</i>",
                parse_mode="HTML",
            )
        except Exception:
            pass  # Message may have been deleted or too old to edit

    async def _on_error(
        self, update: object, context: ContextTypes.DEFAULT_TYPE
    ) -> None:
        logger.error("Telegram error: %s", context.error)

    # ------------------------------------------------------------------
    # Typing indicator
    # ------------------------------------------------------------------

    def _start_typing(self, chat_id: str) -> None:
        self._stop_typing(chat_id)
        self._typing_tasks[chat_id] = asyncio.create_task(
            self._typing_loop(chat_id), name=f"typing-{chat_id}"
        )

    def _stop_typing(self, chat_id: str) -> None:
        task = self._typing_tasks.pop(chat_id, None)
        if task and not task.done():
            task.cancel()

    async def _typing_loop(self, chat_id: str) -> None:
        """Repeatedly send typing action every 4 seconds until cancelled."""
        try:
            while self._app:
                await self._app.bot.send_chat_action(
                    chat_id=int(chat_id), action="typing"
                )
                await asyncio.sleep(4)
        except asyncio.CancelledError:
            pass
        except Exception as e:
            logger.debug("Typing indicator error for %s: %s", chat_id, e)

    # ------------------------------------------------------------------
    # Message sending
    # ------------------------------------------------------------------

    async def _send_text(self, chat_id: int, content: str) -> None:
        """Send text to a Telegram chat, splitting if necessary."""
        if not self._app or not content:
            return
        for chunk in split_message(content):
            try:
                html = markdown_to_telegram_html(chunk)
                await self._app.bot.send_message(
                    chat_id=chat_id,
                    text=html,
                    parse_mode="HTML",
                )
            except Exception as e:
                logger.warning("HTML send failed, retrying as plain text: %s", e)
                try:
                    await self._app.bot.send_message(chat_id=chat_id, text=chunk)
                except Exception as e2:
                    logger.error("Failed to send message to %s: %s", chat_id, e2)

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def _find_chat_id_for_thread(self, thread_id: str) -> int | None:
        """Find the Telegram chat_id associated with a DotCraft thread_id."""
        # Reverse-look up: find the identity_key whose thread_id matches
        for identity_key, tid in self._thread_map.items():
            if tid == thread_id:
                # identity_key is "user_id:channel_context"
                # channel_context is the chat_id string
                parts = identity_key.split(":", 1)
                if len(parts) == 2:
                    return self._parse_target_chat_id(parts[1])
        return None

    def _parse_target_chat_id(self, target: str) -> int | None:
        chat_id_str = str(target).removeprefix("group:").removeprefix("user:")
        try:
            return int(chat_id_str)
        except ValueError:
            return None

    async def _send_document_from_message(
        self,
        chat_id: int,
        message: dict,
        source: dict,
        file_name: str,
        caption: str,
    ):
        if not self._app:
            raise RuntimeError("Telegram bot not running.")

        source_kind = str(source.get("kind", ""))
        if source_kind == "hostPath":
            host_path = str(source.get("hostPath", ""))
            with open(host_path, "rb") as handle:
                return await self._app.bot.send_document(
                    chat_id=chat_id,
                    document=handle,
                    caption=caption or None,
                    filename=file_name,
                )

        if source_kind == "url":
            return await self._app.bot.send_document(
                chat_id=chat_id,
                document=str(source.get("url", "")),
                caption=caption or None,
                filename=file_name,
            )

        if source_kind == "dataBase64":
            raw = base64.b64decode(str(source.get("dataBase64", "")))
            buffer = io.BytesIO(raw)
            buffer.name = file_name
            return await self._app.bot.send_document(
                chat_id=chat_id,
                document=buffer,
                caption=caption or None,
                filename=file_name,
            )

        raise ValueError(
            f"Telegram example cannot send file source kind '{source_kind}'."
        )
