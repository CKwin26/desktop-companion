import type { IntentInput, IntentParseResult } from "./intent";
import type { ReplyInput, ReplyResult } from "./reply";
import type { ReviewDigest } from "./review";
import type { CompanionState } from "./companion";
import type { Task } from "./task";

export interface ReviewInput {
  tasks: Task[];
  companion: CompanionState;
}

export interface AIProvider {
  name: string;
  isAvailable(): Promise<boolean>;
  parseIntent(input: IntentInput): Promise<IntentParseResult>;
  generateReply(input: ReplyInput): Promise<ReplyResult>;
  buildReview(input: ReviewInput): Promise<ReviewDigest>;
}
