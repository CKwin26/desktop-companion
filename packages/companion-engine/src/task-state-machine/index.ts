import type { Task, TaskPriority, TaskStatus, TaskTransitionInput } from "@desktop-companion/shared-types";

const allowedTransitions: Record<TaskStatus, TaskStatus[]> = {
  todo: ["doing", "blocked", "done", "snoozed"],
  doing: ["todo", "blocked", "done", "snoozed"],
  blocked: ["todo", "doing", "done", "snoozed"],
  snoozed: ["todo", "doing"],
  done: ["todo"],
};

export interface CreateTaskInput {
  title: string;
  priority?: TaskPriority;
  source?: Task["source"];
  createdAt?: string;
}

export function createTask(input: CreateTaskInput): Task {
  const now = input.createdAt ?? new Date().toISOString();

  return {
    id: crypto.randomUUID(),
    title: input.title.trim(),
    status: "todo",
    priority: input.priority ?? "medium",
    createdAt: now,
    updatedAt: now,
    tags: [],
    source: input.source ?? "quick-input",
  };
}

export function transitionTask(task: Task, input: TaskTransitionInput): Task {
  if (!allowedTransitions[task.status].includes(input.nextStatus)) {
    throw new Error(`Transition from ${task.status} to ${input.nextStatus} is not allowed.`);
  }

  return {
    ...task,
    status: input.nextStatus,
    latestNote: input.note ?? task.latestNote,
    updatedAt: input.at,
  };
}
