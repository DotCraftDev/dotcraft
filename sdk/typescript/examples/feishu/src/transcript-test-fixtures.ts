export type WireEventFixture = {
  method: string;
  params: Record<string, unknown>;
};

export type TranscriptFixture = {
  turnId: string;
  threadId: string;
  channelContext: string;
  events: WireEventFixture[];
  expectedFinalTranscript: string;
};

export const twoApprovalFileSendFixture: TranscriptFixture = {
  turnId: "turn-fixture-1",
  threadId: "thread-fixture-1",
  channelContext: "dm:test-user",
  events: [
    {
      method: "item/started",
      params: { threadId: "thread-fixture-1", item: { id: "agent-1", type: "agentMessage" } },
    },
    {
      method: "item/agentMessage/delta",
      params: {
        threadId: "thread-fixture-1",
        itemId: "agent-1",
        delta: "我来测试文件。首先我需要确认文件路径是否正确，然后文件发送工具。\n\n",
      },
    },
    {
      method: "item/started",
      params: { threadId: "thread-fixture-1", item: { id: "tool-1", type: "toolCall" } },
    },
    {
      method: "item/agentMessage/delta",
      params: {
        threadId: "thread-fixture-1",
        itemId: "agent-1",
        delta: "文件存在，内容是一个Windows无人值守安装配置文件。现在使用文件发送将文件发送。\n\n",
      },
    },
    {
      method: "item/started",
      params: { threadId: "thread-fixture-1", item: { id: "ext-tool", type: "externalChannelToolCall" } },
    },
    {
      method: "item/completed",
      params: {
        threadId: "thread-fixture-1",
        item: {
          id: "agent-1",
          type: "agentMessage",
          payload: {
            text:
              "我来测试文件。首先我需要确认文件路径是否正确，然后文件发送工具。\n\n" +
              "文件存在，内容是一个Windows无人值守安装配置文件。现在使用文件发送将文件发送。\n\n",
          },
        },
      },
    },
    {
      method: "item/started",
      params: { threadId: "thread-fixture-1", item: { id: "agent-2", type: "agentMessage" } },
    },
    {
      method: "item/agentMessage/delta",
      params: {
        threadId: "thread-fixture-1",
        itemId: "agent-2",
        delta:
          "已成功将 `C:\\Untitled.xml` 文件发送给你！这是一个Windows无人值守安装配置文件，包含Windows 11 Pro的安装设置。\n\n" +
          "文件发送测试完成，工具工作正常。",
      },
    },
    {
      method: "turn/completed",
      params: {
        threadId: "thread-fixture-1",
        turn: {
          items: [
            {
              id: "agent-1",
              type: "agentMessage",
              payload: {
                text:
                  "我来测试文件。首先我需要确认文件路径是否正确，然后文件发送工具。\n\n" +
                  "文件存在，内容是一个Windows无人值守安装配置文件。现在使用文件发送将文件发送。\n\n",
              },
            },
            {
              id: "agent-2",
              type: "agentMessage",
              payload: {
                text:
                  "已成功将 `C:\\Untitled.xml` 文件发送给你！这是一个Windows无人值守安装配置文件，包含Windows 11 Pro的安装设置。\n\n" +
                  "文件发送测试完成，工具工作正常。",
              },
            },
          ],
        },
      },
    },
  ],
  expectedFinalTranscript:
    "我来测试文件。首先我需要确认文件路径是否正确，然后文件发送工具。\n\n" +
    "文件存在，内容是一个Windows无人值守安装配置文件。现在使用文件发送将文件发送。\n\n" +
    "已成功将 `C:\\Untitled.xml` 文件发送给你！这是一个Windows无人值守安装配置文件，包含Windows 11 Pro的安装设置。\n\n" +
    "文件发送测试完成，工具工作正常。",
};

export const prefixRepairFixture: TranscriptFixture = {
  turnId: "turn-prefix-repair",
  threadId: "thread-prefix-repair",
  channelContext: "dm:test-user",
  events: [
    {
      method: "item/started",
      params: { threadId: "thread-prefix-repair", item: { id: "a1", type: "agentMessage" } },
    },
    {
      method: "item/agentMessage/delta",
      params: {
        threadId: "thread-prefix-repair",
        itemId: "a1",
        delta: "存在，内容是一个Windows无人值守安装配置文件。现在使用文件发送将文件发送。\n\n",
      },
    },
    {
      method: "item/started",
      params: { threadId: "thread-prefix-repair", item: { id: "tool-1", type: "toolCall" } },
    },
    {
      method: "item/completed",
      params: {
        threadId: "thread-prefix-repair",
        item: {
          id: "a1",
          type: "agentMessage",
          payload: {
            text: "文件存在，内容是一个Windows无人值守安装配置文件。现在使用文件发送将文件发送。\n\n",
          },
        },
      },
    },
    {
      method: "turn/completed",
      params: {
        threadId: "thread-prefix-repair",
        turn: {
          items: [
            {
              id: "a1",
              type: "agentMessage",
              payload: {
                text: "文件存在，内容是一个Windows无人值守安装配置文件。现在使用文件发送将文件发送。\n\n",
              },
            },
          ],
        },
      },
    },
  ],
  expectedFinalTranscript: "文件存在，内容是一个Windows无人值守安装配置文件。现在使用文件发送将文件发送。\n\n",
};

export const fileCaptionFixture = {
  caption: "这是您请求的 Untitled.xml 文件",
  expectedCaptionLine: "这是您请求的 Untitled.xml 文件",
};
