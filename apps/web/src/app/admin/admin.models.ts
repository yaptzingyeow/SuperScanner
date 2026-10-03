export type GrantDuration = '1m' | '3m' | '1y' | 'forever';

export interface AdminPage<T> { items: T[]; page: number; pageSize: number; total: number; }

export interface AdminDashboard {
  users: { total: number; signedIn: number; guests: number; new: { today: number; d7: number; d30: number }; active7d: number };
  subscribers: { total: number; manual: number; paid: number };
  documents: { total: number; today: number };
  ocr: { today: number; month: number; estimatedCostMonth: number };
  watermarkExports: { today: number; month: number };
  series: { day: string; newUsers: number; ocrPages: number }[];
  phase: 'Test' | 'Enforced';
  effectivePhase: 'Test' | 'Enforced';
  enforceFromUtc: string | null;
}

export interface AdminUser {
  uid: string; email: string | null; provider: string; isGuest: boolean;
  createdAt: string; lastSeenAt: string; plan: 'Free' | 'Pro';
}

export interface AdminSubscription {
  id: string; accountUid: string; email: string | null; source: string; status: string;
  startsAt: string; endsAt: string | null; note: string | null; grantedByUid: string | null;
  createdAt: string; revokedAt: string | null; active: boolean;
}

export interface AdminUserDetail extends AdminUser {
  isAdmin: boolean;
  documentCount: number;
  usage: { day: string; ocrPages: number; bonusOcrPages: number; watermarkExports: number; resetsAt: string };
  subscriptions: AdminSubscription[];
}

export interface AdminPayment {
  id: string; accountUid: string; provider: string; providerReference: string; amount: number;
  currency: string; status: string; createdAt: string; subscriptionId: string | null;
}

export interface AdminSettings {
  phase: 'Test' | 'Enforced';
  enforceFromUtc: string | null;
  freeOcrPagesPerDay: number;
  freeWatermarkExportsPerDay: number;
  freeMaxDocuments: number;
  freeRetentionDays: number;
  usageTimeZone: string;
  ocrCostPerThousandPages: number;
}

export interface AdminMember { uid: string; email: string | null; addedByUid: string | null; addedAt: string; }
