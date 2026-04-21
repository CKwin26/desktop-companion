import type { AIProvider, IntentInput, ReplyInput, ReviewInput } from "@desktop-companion/shared-types";
import { parseWithRules } from "./rules";
import { buildReply, buildRuleBasedReview } from "./templates";

export class NoneProvider implements AIProvider {
  name = "none";

  async isAvailable() {
    return true;
  }

  async parseIntent(input: IntentInput) {
    return parseWithRules(input);
  }

  async generateReply(input: ReplyInput) {
    return buildReply(input);
  }

  async buildReview(input: ReviewInput) {
    return buildRuleBasedReview(input);
  }
}

export function createNoneProvider() {
  return new NoneProvider();
}
