export type EmotionName =
  | "idle"
  | "happy"
  | "focused"
  | "concerned"
  | "urgent"
  | "sleepy";

export interface EmotionSnapshot {
  name: EmotionName;
  intensity: 1 | 2 | 3;
  reason: string;
  updatedAt: string;
}
