import type { EmotionSnapshot } from "./emotion";

export type ProviderName = "none" | "ollama" | "openai-compatible";

export interface QuietHours {
  start: string;
  end: string;
}

export interface CompanionState {
  petName: string;
  emotion: EmotionSnapshot;
  activeTaskId?: string;
  reminderMinutes: number;
  reviewMinutes: number;
  focusMinutes: number;
  quietHours?: QuietHours;
  provider: ProviderName;
}
