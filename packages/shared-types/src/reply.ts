import type { EmotionSnapshot } from "./emotion";

export interface ReplyInput {
  message: string;
  emotion: EmotionSnapshot;
  petName: string;
}

export interface ReplyResult {
  text: string;
}
