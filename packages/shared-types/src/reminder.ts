export type ReminderChannel = "widget" | "system" | "both";

export interface Reminder {
  id: string;
  taskId?: string;
  title: string;
  remindAt: string;
  repeatRule?: string;
  channel: ReminderChannel;
  dismissedAt?: string;
  createdAt: string;
}
