import type { EmotionSnapshot } from "@desktop-companion/shared-types";

export function composeStatusLine(petName: string, emotion: EmotionSnapshot) {
  const prefix = `${petName}：`;

  switch (emotion.name) {
    case "happy":
      return `${prefix} 有进展了，继续保持。`;
    case "focused":
      return `${prefix} 主线已经点亮，我会继续盯住节奏。`;
    case "concerned":
      return `${prefix} 我闻到一点卡顿，先把阻塞点说清楚。`;
    case "urgent":
      return `${prefix} 该回来看一眼任务列表了。`;
    case "sleepy":
      return `${prefix} 暂时风平浪静，我先打个盹。`;
    default:
      return `${prefix} 随时可以把下一条任务交给我。`;
  }
}
