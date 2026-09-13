import 'dart:convert';
import 'ansight_models.dart';

/// Sanitized input. Never pass a receipt, purchase token or account identifier.
class PurchaseObservation {
  const PurchaseObservation(
      {required this.productId,
      required this.source,
      this.observedAt,
      this.transactionRef,
      this.state = 'unknown',
      this.verification = 'unknown',
      this.entitled,
      this.deliveryCount,
      this.finished,
      this.acknowledged,
      this.consumed,
      this.expiresAt,
      this.gracePeriodExpiresAt,
      this.environment = 'unknown',
      this.productType = 'unknown'});
  final String productId, source, state, verification, environment, productType;
  final int? observedAt, deliveryCount, expiresAt, gracePeriodExpiresAt;
  final String? transactionRef;
  final bool? entitled, finished, acknowledged, consumed;
  AnsightJson toJson() => <String, Object?>{
        'productId': productId,
        'transactionRef': transactionRef,
        'source': source,
        'observedAt': observedAt ?? DateTime.now().millisecondsSinceEpoch,
        'state': state,
        'verification': verification,
        'entitled': entitled,
        'deliveryCount': deliveryCount,
        'finished': finished,
        'acknowledged': acknowledged,
        'consumed': consumed,
        'expiresAt': expiresAt,
        'gracePeriodExpiresAt': gracePeriodExpiresAt,
        'environment': environment,
        'productType': productType,
      };
}

class PurchaseProduct {
  const PurchaseProduct(
      {required this.productId,
      required this.available,
      this.observedAt,
      this.displayPrice,
      this.price,
      this.currencyCode,
      this.productType = 'unknown'});
  final String productId, productType;
  final bool available;
  final int? observedAt;
  final String? displayPrice, price, currencyCode;
  AnsightJson toJson() => <String, Object?>{
        'productId': productId,
        'available': available,
        'observedAt': observedAt ?? DateTime.now().millisecondsSinceEpoch,
        'displayPrice': displayPrice,
        'price': price,
        'currencyCode': currencyCode,
        'productType': productType,
      };
}

/// Thin facade; validation, bounded storage and remote tools belong to native core.
class PurchaseDiagnostics {
  PurchaseDiagnostics(this._invoke);
  final Future<AnsightJson> Function(String, AnsightJson?) _invoke;
  Future<AnsightJson> _command(String action,
      [AnsightJson fields = const {}]) async {
    final response = await _invoke('purchaseCommand', <String, Object?>{
      'json': jsonEncode(<String, Object?>{'action': action, ...fields})
    });
    return Map<String, Object?>.from(
        jsonDecode(response['json']! as String) as Map);
  }

  Future<AnsightJson> createTransactionReference(String identifier) =>
      _command('reference', {'identifier': identifier});
  Future<AnsightJson> register() => _command('register');
  Future<AnsightJson> record(PurchaseObservation observation) =>
      _command('record', {'observation': observation.toJson()});
  Future<AnsightJson> recordProduct(PurchaseProduct product) =>
      _command('recordProduct', {'product': product.toJson()});

  /// Clear retained evidence on account switch; leaves the store untouched.
  Future<AnsightJson> clear() => _command('clear');
  Future<AnsightJson> snapshot({String? productId}) =>
      _command('purchases.get_state', {
        'arguments': {if (productId != null) 'productId': productId}
      });
  Future<AnsightJson> validate(
          {required String productId,
          required bool expectedEntitled,
          String? transactionRef,
          bool requireVerified = true,
          bool requireBackendVerification = false,
          int? expectedDeliveryCount,
          int maxAgeMilliseconds = 60000}) =>
      _command('purchases.validate', {
        'arguments': {
          'productId': productId,
          'expectedEntitled': expectedEntitled,
          if (transactionRef != null) 'transactionRef': transactionRef,
          'requireVerified': requireVerified,
          'requireBackendVerification': requireBackendVerification,
          if (expectedDeliveryCount != null)
            'expectedDeliveryCount': expectedDeliveryCount,
          'maxAgeMilliseconds': maxAgeMilliseconds
        }
      });
  Future<AnsightJson> refreshStoreKit(List<String> productIds) =>
      _command('refreshStoreKit', {'productIds': productIds});
}
