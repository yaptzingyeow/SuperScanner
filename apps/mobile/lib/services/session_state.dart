import 'package:flutter/foundation.dart';

import '../api/api_client.dart';
import '../api/models.dart';

/// The signed-in user's plan, loaded after sign-in. Free keeps documents on the phone; Pro syncs to the cloud.
class SessionState extends ChangeNotifier {
  SessionState(this._documents);

  final DocumentsApi _documents;
  PlanInfo? _plan;
  Object? _error;

  PlanInfo? get plan => _plan;
  Object? get error => _error;
  bool get isPro => _plan?.isPro ?? false;

  Future<void> refresh() async {
    try {
      _plan = await _documents.getPlan();
      _error = null;
    } catch (error) {
      _error = error;
    }
    notifyListeners();
  }

  Future<void> acceptPrivacyNotice() async {
    final version = _plan?.currentPrivacyVersion;
    if (version == null) return;
    await _documents.recordConsent(version);
    await refresh();
  }

  void clear() {
    _plan = null;
    _error = null;
    notifyListeners();
  }
}
