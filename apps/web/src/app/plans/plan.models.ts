export type PlanKind = 'Free' | 'Pro';
export type PlanPhase = 'Test' | 'Enforced';

export interface MePlan {
  plan: PlanKind;
  phase: PlanPhase;
  proUntil: string | null;
  proForever: boolean;
  brandStamp: boolean;
  isAdmin?: boolean;
  timeZone?: string | null;
  locale?: string | null;
  privacyConsentVersion?: string | null;
  currentPrivacyVersion?: string;
  limits: {
    ocrPagesPerDay: number | null;
    watermarkExportsPerDay: number | null;
    maxDocuments: number | null;
    retentionDays: number | null;
  };
  usage: { ocrPages: number; bonusOcrPages: number; watermarkExports: number; resetsAt: string };
  documentCount: number;
}

/** The 429 body every limited endpoint returns. */
export interface PlanLimitProblem {
  code: 'plan_limit_reached';
  kind: 'ocr' | 'watermark' | 'documents';
  limit: number;
  used: number;
  resetsAt: string | null;
}

export interface UsageMeter {
  used: number;
  limit: number;
}
