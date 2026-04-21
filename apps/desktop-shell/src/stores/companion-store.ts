import { createNoneProvider } from "@desktop-companion/ai-provider-none";
import {
  buildReview,
  composeStatusLine,
  createEmptySnapshot,
  createTask,
  deriveEmotion,
} from "@desktop-companion/companion-engine";
import type { ReviewDigest, Task } from "@desktop-companion/shared-types";

function seedTasks(): Task[] {
  return [
    createTask({
      title: "定义桌面伴侣 MVP 范围",
      priority: "high",
      source: "panel",
    }),
    createTask({
      title: "拆出 Shell / Engine / Provider 分层",
      priority: "medium",
      source: "panel",
    }),
  ];
}

export async function getShellViewModel() {
  const provider = createNoneProvider();
  const snapshot = createEmptySnapshot("团子");
  const tasks = seedTasks();
  const emotion = deriveEmotion(tasks);
  const reply = await provider.generateReply({
    message: "桌面壳已经接上了最小骨架。",
    emotion,
    petName: snapshot.companion.petName,
  });

  snapshot.tasks = tasks;
  snapshot.companion.emotion = emotion;

  const review: ReviewDigest = buildReview(tasks, snapshot.companion);
  const topTask = tasks.find((task) => task.status === "doing") ?? tasks[0];
  const taskCount = tasks.length;
  const doneCount = tasks.filter((task) => task.status === "done").length;
  const blockedCount = tasks.filter((task) => task.status === "blocked").length;

  return {
    petName: snapshot.companion.petName,
    emotion,
    statusLine: composeStatusLine(snapshot.companion.petName, emotion),
    reply: reply.text,
    review,
    tasks,
    topTask,
    taskCount,
    doneCount,
    blockedCount,
  };
}
