import type { CompanionState, Reminder, Task } from "@desktop-companion/shared-types";

export interface EngineSnapshot {
  tasks: Task[];
  reminders: Reminder[];
  companion: CompanionState;
}

export function createEmptySnapshot(companionName = "团子"): EngineSnapshot {
  return {
    tasks: [],
    reminders: [],
    companion: {
      petName: companionName,
      emotion: {
        name: "idle",
        intensity: 1,
        reason: "等待新的任务或状态更新。",
        updatedAt: new Date().toISOString(),
      },
      reminderMinutes: 45,
      reviewMinutes: 120,
      focusMinutes: 25,
      provider: "none",
    },
  };
}
