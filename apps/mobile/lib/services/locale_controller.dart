import 'dart:io';

import 'package:flutter/widgets.dart';

/// The app language: the phone's language when supported, or the user's choice in Settings.
/// Same four languages as the website; Arabic lays the app out right-to-left.
class LocaleController extends ChangeNotifier {
  LocaleController(this._file);

  static const supported = [Locale('en'), Locale('ms'), Locale('zh'), Locale('ar')];
  static const names = {'en': 'English', 'ms': 'Bahasa Melayu', 'zh': '中文（简体）', 'ar': 'العربية'};

  final File _file;
  Locale? _chosen;

  /// Null means "follow the phone".
  Locale? get locale => _chosen;

  Future<void> load() async {
    if (!await _file.exists()) return;
    final code = (await _file.readAsString()).trim();
    if (names.containsKey(code)) _chosen = Locale(code);
  }

  Future<void> choose(String? code) async {
    _chosen = code == null ? null : Locale(code);
    if (code == null) {
      if (await _file.exists()) await _file.delete();
    } else {
      await _file.parent.create(recursive: true);
      await _file.writeAsString(code, flush: true);
    }
    notifyListeners();
  }

  /// Picks the closest supported language for the phone's language list.
  static Locale resolve(List<Locale>? phone) {
    for (final locale in phone ?? const <Locale>[]) {
      if (names.containsKey(locale.languageCode)) return Locale(locale.languageCode);
    }
    return const Locale('en');
  }
}
