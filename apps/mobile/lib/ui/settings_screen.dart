import 'dart:io';

import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';
import 'package:provider/provider.dart';
import 'package:share_plus/share_plus.dart';

import '../api/api_client.dart';
import '../services/auth_service.dart';
import '../services/local_store.dart';
import '../services/locale_controller.dart';
import '../services/session_state.dart';
import 'common.dart';

/// Account, plan, language and privacy: download my data and delete my account (both required by the app stores).
class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key});

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  bool _busy = false;

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _downloadData() {
    final subject = context.l.myDataSubject;
    final api = context.read<DocumentsApi>().client;
    return _run(() async {
      final bytes = await api.getBytes('/me/export');
      final file = File('${(await getTemporaryDirectory()).path}/arks-scanner-my-data.zip');
      await file.writeAsBytes(bytes, flush: true);
      await SharePlus.instance.share(ShareParams(files: [XFile(file.path)], subject: subject));
    });
  }

  Future<void> _deleteAccount() async {
    final controller = TextEditingController();
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(context.l.deleteAccountQuestion),
        content: Column(mainAxisSize: MainAxisSize.min, children: [
          Text(context.l.deleteAccountWarning),
          TextField(controller: controller, autofocus: true),
        ]),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: Text(context.l.cancel)),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: Brand.danger),
            onPressed: () => Navigator.pop(context, controller.text.trim() == 'DELETE'),
            child: Text(context.l.deleteAccount),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    final api = context.read<DocumentsApi>().client;
    final local = context.read<LocalStore>();
    final auth = context.read<AuthService>();
    await _run(() async {
      await api.deleteWithBody('/me', {'confirm': 'DELETE'});
      await local.clear();
      await auth.signOut();
    });
  }

  Future<void> _chooseLanguage() async {
    final locales = context.read<LocaleController>();
    final current = locales.locale?.languageCode;
    final choice = await showDialog<String>(
      context: context,
      builder: (context) => SimpleDialog(
        title: Text(context.l.language),
        children: [
          RadioGroup<String>(
            groupValue: current ?? '',
            onChanged: (value) => Navigator.pop(context, value),
            child: Column(children: [
              for (final entry in [MapEntry('', context.l.phoneLanguage), ...LocaleController.names.entries])
                RadioListTile<String>(value: entry.key, title: Text(entry.value)),
            ]),
          ),
        ],
      ),
    );
    if (choice != null) await locales.choose(choice.isEmpty ? null : choice);
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthService>();
    final session = context.watch<SessionState>();
    final language = context.watch<LocaleController>().locale?.languageCode;
    final l = context.l;
    return Scaffold(
      appBar: AppBar(title: Text(l.settingsPrivacy)),
      body: ListView(children: [
        if (_busy) const LinearProgressIndicator(),
        ListTile(leading: const Icon(Icons.person_outline), title: Text(auth.email ?? l.signedIn)),
        ListTile(
          leading: const Icon(Icons.workspace_premium_outlined),
          title: Text(session.isPro ? l.proPlan : l.freePlan),
          subtitle: Text(session.isPro ? l.proPlanDetail : l.freePlanDetail),
        ),
        ListTile(
          leading: const Icon(Icons.translate),
          title: Text(l.language),
          subtitle: Text(language == null ? l.phoneLanguage : LocaleController.names[language]!),
          onTap: _chooseLanguage,
        ),
        const Divider(),
        ListTile(leading: const Icon(Icons.download_outlined), title: Text(l.downloadMyData),
            onTap: _busy ? null : _downloadData),
        ListTile(
          leading: const Icon(Icons.delete_forever_outlined, color: Brand.danger),
          title: Text(l.deleteMyAccount, style: const TextStyle(color: Brand.danger)),
          onTap: _busy ? null : _deleteAccount,
        ),
        const Divider(),
        AboutListTile(applicationName: l.appName, applicationLegalese: 'ArkSoft', icon: const Icon(Icons.info_outline)),
      ]),
    );
  }
}
