export type TaskStatus = "todo" | "doing" | "blocked" | "done" | "snoozed";
export type TaskPriority = "low" | "medium" | "high";
export type TaskSource = "quick-input" | "panel" | "ai";

export interface Task {
  id: string;
  title: string;
  description?: string;
  status: TaskStatus;
  priority: TaskPriority;
  createdAt: string;
  updatedAt: string;
  dueAt?: string;
  snoozedUntil?: string;
  estimatedMinutes?: number;
  category?: string;
  tags: string[];
  latestNote?: string;
  source: TaskSource;
}

export interface TaskTransitionInput {
  taskId: string;
  nextStatus: TaskStatus;
  note?: string;
  at: string;
}
