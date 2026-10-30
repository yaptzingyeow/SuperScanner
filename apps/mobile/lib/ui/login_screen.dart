import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../l10n/app_localizations.dart';
import '../services/auth_service.dart';
import 'common.dart';

/// Welcome and sign-in. Sign-in is required before scanning.
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _creating = false;
  bool _busy = false;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _run(Future<void> Function() action) async {
    final l = context.l;
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _busy = true);
    try {
      await action();
    } on FirebaseAuthException catch (error) {
      messenger.showSnackBar(SnackBar(content: Text(_authMessage(l, error.code))));
    } catch (_) {
      messenger.showSnackBar(SnackBar(content: Text(l.authFailed)));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  static String _authMessage(AppLocalizations l, String code) => switch (code) {
        'invalid-credential' || 'wrong-password' || 'user-not-found' => l.authIncorrect,
        'email-already-in-use' => l.authEmailInUse,
        'weak-password' => l.authWeakPassword,
        'invalid-email' => l.authInvalidEmail,
        'network-request-failed' => l.authNoConnection,
        'web-context-canceled' || 'canceled' => l.authCancelled,
        _ => l.authFailed,
      };

  @override
  Widget build(BuildContext context) {
    final auth = context.read<AuthService>();
    final l = context.l;
    return Scaffold(
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            const SizedBox(height: 32),
            Image.asset('assets/brand/arks-scanner-mark.png', height: 72),
            const SizedBox(height: 16),
            Text(l.appName, textAlign: TextAlign.center, style: Theme.of(context).textTheme.headlineMedium),
            const SizedBox(height: 8),
            Text(l.tagline, textAlign: TextAlign.center),
            const SizedBox(height: 32),
            FilledButton.icon(
              onPressed: _busy ? null : () => _run(auth.signInWithGoogle),
              icon: const Icon(Icons.account_circle_outlined),
              label: Text(l.continueWithGoogle),
            ),
            const SizedBox(height: 24),
            Row(children: [
              const Expanded(child: Divider()),
              Padding(padding: const EdgeInsets.all(8), child: Text(l.or)),
              const Expanded(child: Divider()),
            ]),
            const SizedBox(height: 16),
            TextField(controller: _email, keyboardType: TextInputType.emailAddress, autocorrect: false,
                decoration: InputDecoration(labelText: l.email, border: const OutlineInputBorder())),
            const SizedBox(height: 12),
            TextField(controller: _password, obscureText: true,
                decoration: InputDecoration(labelText: l.password, border: const OutlineInputBorder())),
            const SizedBox(height: 16),
            OutlinedButton(
              onPressed: _busy
                  ? null
                  : () => _run(() => _creating
                      ? auth.createAccount(_email.text, _password.text)
                      : auth.signInWithEmail(_email.text, _password.text)),
              child: Text(_creating ? l.createAccount : l.signInWithEmail),
            ),
            TextButton(
              onPressed: _busy ? null : () => setState(() => _creating = !_creating),
              child: Text(_creating ? l.haveAccount : l.createAnAccount),
            ),
            if (!_creating)
              TextButton(
                onPressed: _busy
                    ? null
                    : () {
                        if (_email.text.trim().isEmpty) {
                          showMessage(context, l.enterEmailFirst);
                          return;
                        }
                        final messenger = ScaffoldMessenger.of(context);
                        _run(() async {
                          await auth.sendPasswordReset(_email.text);
                          messenger.showSnackBar(SnackBar(content: Text(l.resetSent)));
                        });
                      },
                child: Text(l.forgotPassword),
              ),
          ],
        ),
      ),
    );
  }
}
