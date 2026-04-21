export interface ReviewDigest {
  id: string;
  createdAt: string;
  headline: string;
  summary: string;
  bullets: string[];
  topTaskId?: string;
  riskTaskIds: string[];
}
