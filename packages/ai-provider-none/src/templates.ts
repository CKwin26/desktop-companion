import type { EmotionSnapshot, ReplyInput, ReplyResult, ReviewDigest, ReviewInput } from "@desktop-companion/shared-types";

function emotionPrefix(emotion: EmotionSnapshot) {
  switch (emotion.name) {
    case "happy":
      return "好耶";
    case "focused":
      return "收到";
    case "concerned":
      return "嗯，这里有点不对劲";
    case "urgent":
      return "先停一下";
    case "sleepy":
      return "我还醒着";
    default:
      return "我在";
  }
}

export function buildReply(input: ReplyInput): ReplyResult {
  return {
    text: `${emotionPrefix(input.emotion)}，${input.petName} 已记录：${input.message}`,
  };
}

export function buildRuleBasedReview(input: ReviewInput): ReviewDigest {
  const doing = input.tasks.filter((task) => task.status === "doing");
  const blocked = input.tasks.filter((task) => task.status === "blocked");
  const done = input.tasks.filter((task) => task.status === "done");
  const topTask = doing[0] ?? input.tasks.find((task) => task.status === "todo");

  return {
    id: crypto.randomUUID(),
    createdAt: new Date().toISOString(),
    headline: blocked.length > 0 ? "先解阻，再冲刺。" : "主线要尽量保持单一。",
    summary: blocked.length > 0 ? "存在阻塞任务，建议先写出原因。" : "如果还没开始，就先从一件最重要的事切入。",
    bullets: [
      topTask ? `最优先看「${topTask.title}」。` : "先创建第一件任务。",
      `推进中 ${doing.length} 件，阻塞 ${blocked.length} 件，完成 ${done.length} 件。`,
      "这份梳理由 none provider 的规则模板生成，后面可以用本地或远程模型增强。",
    ],
    topTaskId: topTask?.id,
    riskTaskIds: blocked.map((task) => task.id),
  };
}
