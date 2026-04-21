import type { TaskPriority, TaskStatus } from "./task";

export type IntentKind =
  | "create-task"
  | "update-task-status"
  | "create-reminder"
  | "snooze-task"
  | "unknown";

export interface IntentInput {
  text: string;
  now: string;
}

export interface IntentParseResult {
  kind: IntentKind;
  confidence: number;
  title?: string;
  taskId?: string;
  nextStatus?: TaskStatus;
  priority?: TaskPriority;
  reminderAt?: string;
  reason?: string;
}
