"""
Entry point: python -m dotcraft_telegram

Reads configuration from environment variables:
  TELEGRAM_BOT_TOKEN   (required) Telegram bot token from @BotFather
  DOTCRAFT_WORKSPACE   (optional) Workspace path passed to thread/start
  HTTPS_PROXY          (optional) HTTPS proxy URL for Telegram API
"""

import asyncio
import logging
import os
import sys

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)-8s %(name)s: %(message)s",
    stream=sys.stderr,
)

from .bot import TelegramAdapter


async def main() -> None:
    token = os.environ.get("TELEGRAM_BOT_TOKEN", "")
    if not token:
        logging.error(
            "TELEGRAM_BOT_TOKEN environment variable is not set. "
            "Get a token from @BotFather on Telegram."
        )
        sys.exit(1)

    workspace = os.environ.get("DOTCRAFT_WORKSPACE", "")
    proxy = os.environ.get("HTTPS_PROXY") or os.environ.get("https_proxy")

    adapter = TelegramAdapter(
        bot_token=token,
        workspace_path=workspace,
        proxy=proxy,
    )

    try:
        await adapter.run()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    asyncio.run(main())
