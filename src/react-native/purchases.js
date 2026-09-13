"use strict";

// All observation retention, validation and tools live in the native base SDK.
function createPurchaseDiagnostics(send) {
  const command = async (action, fields = {}) => JSON.parse(await send(JSON.stringify({ action, ...fields })));
  return {
    createTransactionReference: (identifier) => command("reference", { identifier }),
    register: () => command("register"),
    record: (observation) => command("record", { observation: { observedAt: Date.now(), ...observation } }),
    recordProduct: (product) => command("recordProduct", { product: { observedAt: Date.now(), productType: "unknown", ...product } }),
    clear: () => command("clear"),
    snapshot: (productId) => command("purchases.get_state", { arguments: productId === undefined ? {} : { productId } }),
    validate: (expectations) => command("purchases.validate", { arguments: expectations }),
    refreshStoreKit: (productIds) => command("refreshStoreKit", { productIds }),
  };
}
module.exports = { createPurchaseDiagnostics };
