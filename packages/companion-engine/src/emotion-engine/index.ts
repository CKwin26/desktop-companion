import type { EmotionSnapshot, Task } from "@desktop-companion/shared-types";

export function deriveEmotion(tasks: Task[]): EmotionSnapshot {
  const now = new Date().toISOString();
  const hasBlocked = tasks.some((task) => task.status === "blocked");
  const hasDoing = tasks.some((task) => task.status === "doing");
  const doneCount = tasks.filter((task) => task.status === "done").length;

  if (hasBlocked) {
    return {
      name: "concerned",
      intensity: 2,
      reason: "存在阻塞任务，需要先拆解或求助。",
      updatedAt: now,
    };
  }

  if (hasDoing) {
    return {
      name: "focused",
      intensity: 2,
      reason: "当前有主线任务正在推进。",
      updatedAt: now,
    };
  }

  if (doneCount > 0) {
    return {
      name: "happy",
      intensity: 2,
      reason: "已经完成了一部分任务。",
      updatedAt: now,
    };
  }

  return {
    name: "idle",
    intensity: 1,
    reason: "等待新的任务或状态更新。",
    updatedAt: now,
  };
}
