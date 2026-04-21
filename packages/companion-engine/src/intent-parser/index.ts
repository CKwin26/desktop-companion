import type { AIProvider, IntentInput, IntentParseResult } from "@desktop-companion/shared-types";

export async function parseIntent(provider: AIProvider, input: IntentInput): Promise<IntentParseResult> {
  return provider.parseIntent(input);
}
