import 'package:firebase_app_check/firebase_app_check.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/foundation.dart';

import '../api/api_client.dart';

/// Firebase sign-in for the app. Sign-in is required before scanning (no guest mode).
class AuthService extends ChangeNotifier implements TokenSource {
  AuthService(this._auth, this._appCheck) {
    _auth.authStateChanges().listen((user) {
      _user = user;
      _ready = true;
      notifyListeners();
    });
  }

  final FirebaseAuth _auth;
  final FirebaseAppCheck _appCheck;
  User? _user;
  bool _ready = false;

  bool get isReady => _ready;
  bool get isSignedIn => _user != null && !_user!.isAnonymous;
  String? get email => _user?.email;
  String? get uid => _user?.uid;

  /// Google sign-in through Firebase's own provider flow (Custom Tabs on Android, web view on iOS).
  Future<void> signInWithGoogle() => _auth.signInWithProvider(GoogleAuthProvider());

  Future<void> signInWithEmail(String email, String password) =>
      _auth.signInWithEmailAndPassword(email: email.trim(), password: password);

  Future<void> createAccount(String email, String password) =>
      _auth.createUserWithEmailAndPassword(email: email.trim(), password: password);

  Future<void> sendPasswordReset(String email) => _auth.sendPasswordResetEmail(email: email.trim());

  Future<void> signOut() => _auth.signOut();

  @override
  Future<String> idToken() async {
    final token = await _user?.getIdToken();
    if (token == null) throw const ApiException(401, 'signed_out');
    return token;
  }

  @override
  Future<String> appCheckToken() async {
    final token = await _appCheck.getToken();
    if (token == null) throw const ApiException(401, 'app_check_unavailable');
    return token;
  }
}
