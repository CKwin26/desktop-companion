import type { IntentInput, IntentParseResult } from "@desktop-companion/shared-types";

function parseRelativeReminder(text: string, now: Date) {
  const halfHourPattern = /(半小时|30分钟)/;
  const hourPattern = /([1-9]\d*)\s*小时/;
  const minutePattern = /([1-9]\d*)\s*分钟/;

  if (halfHourPattern.test(text)) {
    return new Date(now.getTime() + 30 * 60 * 1000).toISOString();
  }

  const hourMatch = text.match(hourPattern);
  if (hourMatch) {
    const hours = Number(hourMatch[1]);
    return new Date(now.getTime() + hours * 60 * 60 * 1000).toISOString();
  }

  const minuteMatch = text.match(minutePattern);
  if (minuteMatch) {
    const minutes = Number(minuteMatch[1]);
    return new Date(now.getTime() + minutes * 60 * 1000).toISOString();
  }

  return undefined;
}

export function parseWithRules(input: IntentInput): IntentParseResult {
  const text = input.text.trim();
  const now = new Date(input.now);

  if (!text) {
    return {
      kind: "unknown",
      confidence: 0,
      reason: "输入为空。",
    };
  }

  if (text.includes("提醒")) {
    return {
      kind: "create-reminder",
      confidence: 0.8,
      title: text.replace(/^提醒我?/, "").trim() || "新的提醒",
      reminderAt: parseRelativeReminder(text, now),
    };
  }

  if (text.includes("卡住") || text.includes("阻塞")) {
    return {
      kind: "update-task-status",
      confidence: 0.75,
      nextStatus: "blocked",
    };
  }

  if (text.includes("完成")) {
    return {
      kind: "update-task-status",
      confidence: 0.75,
      nextStatus: "done",
    };
  }

  if (text.includes("先放一放") || text.includes("稍后") || text.includes("延期")) {
    return {
      kind: "snooze-task",
      confidence: 0.72,
      nextStatus: "snoozed",
    };
  }

  if (text.includes("开始")) {
    return {
      kind: "update-task-status",
      confidence: 0.7,
      nextStatus: "doing",
    };
  }

  return {
    kind: "create-task",
    confidence: 0.6,
    title: text,
    priority: text.includes("最重要") || text.includes("优先") ? "high" : "medium",
  };
}
