import type { CompanionState, ReviewDigest, Task } from "@desktop-companion/shared-types";

export function buildReview(tasks: Task[], companion: CompanionState): ReviewDigest {
  const todoCount = tasks.filter((task) => task.status === "todo").length;
  const doingCount = tasks.filter((task) => task.status === "doing").length;
  const blockedTasks = tasks.filter((task) => task.status === "blocked");
  const doneCount = tasks.filter((task) => task.status === "done").length;
  const topTask = tasks.find((task) => task.status === "doing") ?? tasks.find((task) => task.status === "todo");

  let headline = `${companion.petName} 正在等你给今天定主线。`;
  let summary = "先挑出最重要的一件事开始，再让其他任务排队。";

  if (blockedTasks.length > 0) {
    headline = `先解阻，当前有 ${blockedTasks.length} 个任务卡住。`;
    summary = "优先写出阻塞点，再决定延期、拆分还是求助。";
  } else if (doingCount > 0) {
    headline = `节奏还不错，当前有 ${doingCount} 个任务在推进。`;
    summary = "继续收束主线，别同时点亮太多目标。";
  } else if (doneCount > 0 && todoCount === 0) {
    headline = "列表已经清空，可以开始收尾了。";
    summary = "补一条复盘，再给明天留一个启动动作。";
  }

  return {
    id: crypto.randomUUID(),
    createdAt: new Date().toISOString(),
    headline,
    summary,
    bullets: [
      topTask ? `当前最值得盯住的是「${topTask.title}」。` : "先创建第一条任务，监督节奏就能跑起来。",
      `待办 ${todoCount} 件，推进中 ${doingCount} 件，完成 ${doneCount} 件。`,
      blockedTasks.length > 0 ? `阻塞任务：${blockedTasks.map((task) => task.title).join("、")}。` : "当前没有显式阻塞任务。",
    ],
    topTaskId: topTask?.id,
    riskTaskIds: blockedTasks.map((task) => task.id),
  };
}
