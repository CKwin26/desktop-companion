import type { EmotionSnapshot } from "@desktop-companion/shared-types";

export function createStatusCard(emotion: EmotionSnapshot, statusLine: string, reply: string) {
  const element = document.createElement("section");
  element.className = "status-card";
  element.innerHTML = `
    <p class="status-chip">情绪：${emotion.name}</p>
    <h2 class="status-line">${statusLine}</h2>
    <p class="status-reply">${reply}</p>
  `;

  return element;
}
