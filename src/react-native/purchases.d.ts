export interface PurchaseObservation {
  productId: string;
  source: "store" | "app" | "backend";
  transactionRef?: string | null;
  observedAt?: number;
  state?: "unknown" | "pending" | "purchased" | "cancelled" | "expired" | "revoked" | "failed" | "gracePeriod" | "billingRetry";
  verification?: "unknown" | "verified" | "unverified";
  entitled?: boolean | null;
  deliveryCount?: number | null;
  finished?: boolean | null;
  acknowledged?: boolean | null;
  consumed?: boolean | null;
  expiresAt?: number | null;
  gracePeriodExpiresAt?: number | null;
  environment?: "unknown" | "xcode" | "sandbox" | "production";
  productType?: "unknown" | "consumable" | "nonConsumable" | "subscription" | "nonRenewingSubscription";
}
export interface PurchaseProduct {
  productId: string;
  available: boolean;
  observedAt?: number;
  displayPrice?: string | null;
  price?: string | null;
  currencyCode?: string | null;
  productType?: PurchaseObservation["productType"];
}
export interface PurchaseExpectations {
  transactionRef?: string;
  productId: string;
  expectedEntitled: boolean;
  requireVerified?: boolean;
  requireBackendVerification?: boolean;
  expectedDeliveryCount?: number;
  maxAgeMilliseconds?: number;
}
export interface PurchaseSnapshot {
  schema: "ansight.purchases.v1";
  capturedAt: number;
  droppedEvents: number;
  coverage: "observedOnly";
  observations: PurchaseObservation[];
  products: PurchaseProduct[];
}
export interface PurchaseValidationReport extends PurchaseSnapshot {
  status: "pass" | "fail" | "inconclusive";
  checks: { source: string; status: "pass" | "fail" | "inconclusive"; reason: string }[];
}
export interface PurchaseDiagnostics {
  createTransactionReference(identifier: string): Promise<{ transactionRef: string }>;
  /** Register read-only remote tools after SDK initialization. */
  register(): Promise<{ success: boolean }>;
  record(observation: PurchaseObservation): Promise<{ success: boolean }>;
  recordProduct(product: PurchaseProduct): Promise<{ success: boolean }>;
  /** Clear evidence on account switch. Does not clear store transactions. */
  clear(): Promise<{ success: boolean }>;
  snapshot(productId?: string): Promise<PurchaseSnapshot>;
  validate(expectations: PurchaseExpectations): Promise<PurchaseValidationReport>;
  /** Apple only. Reads StoreKit 2 using known product IDs. */
  refreshStoreKit(productIds: string[]): Promise<PurchaseSnapshot>;
}
export function createPurchaseDiagnostics(send: (json: string) => Promise<string>): PurchaseDiagnostics;
