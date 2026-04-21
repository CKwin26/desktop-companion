import type { Reminder, Task } from "@desktop-companion/shared-types";

export function createDefaultReminder(task: Task, reminderMinutes: number): Reminder {
  const remindAt = new Date(Date.now() + reminderMinutes * 60 * 1000).toISOString();

  return {
    id: crypto.randomUUID(),
    taskId: task.id,
    title: `跟进：${task.title}`,
    remindAt,
    channel: "both",
    createdAt: new Date().toISOString(),
  };
}

export function dueReminders(reminders: Reminder[], nowIso: string) {
  return reminders.filter((reminder) => !reminder.dismissedAt && reminder.remindAt <= nowIso);
}
