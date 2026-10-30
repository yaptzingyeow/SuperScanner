import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';

/// A document saved on this phone only (Free plan storage model).
class LocalDocument {
  const LocalDocument({required this.id, required this.title, required this.savedAt, required this.pageCount});

  final String id;
  final String title;
  final DateTime savedAt;
  final int pageCount;

  Map<String, dynamic> toJson() =>
      {'id': id, 'title': title, 'savedAt': savedAt.toIso8601String(), 'pageCount': pageCount};

  factory LocalDocument.fromJson(Map<String, dynamic> json) => LocalDocument(
        id: json['id'] as String,
        title: json['title'] as String,
        savedAt: DateTime.parse(json['savedAt'] as String),
        pageCount: (json['pageCount'] as num).toInt(),
      );
}

/// Stores phone-only documents in the app's private folder:
/// `<root>/<id>/document.pdf` and `<root>/<id>/page-<n>.jpg`, indexed by `<root>/index.json`.
/// Everything is removed on sign-out so nothing private is left behind.
class LocalStore extends ChangeNotifier {
  LocalStore(this._root);

  final Directory _root;
  List<LocalDocument> _documents = const [];

  List<LocalDocument> get documents => _documents;

  File get _index => File('${_root.path}/index.json');

  Directory folder(String id) => Directory('${_root.path}/$id');
  File pdfFile(String id) => File('${folder(id).path}/document.pdf');
  File pageFile(String id, int number) => File('${folder(id).path}/page-$number.jpg');

  Future<void> load() async {
    if (!await _index.exists()) {
      _documents = const [];
    } else {
      final list = jsonDecode(await _index.readAsString()) as List;
      _documents = [for (final item in list) LocalDocument.fromJson(item as Map<String, dynamic>)]
        ..sort((a, b) => b.savedAt.compareTo(a.savedAt));
    }
    notifyListeners();
  }

  /// Saves a finished document; [pages] are the processed page images in order.
  Future<LocalDocument> save({required String id, required String title, required Uint8List pdf,
      required List<Uint8List> pages, DateTime? now}) async {
    await folder(id).create(recursive: true);
    await pdfFile(id).writeAsBytes(pdf, flush: true);
    for (var index = 0; index < pages.length; index++) {
      await pageFile(id, index + 1).writeAsBytes(pages[index], flush: true);
    }
    final document = LocalDocument(id: id, title: title, savedAt: now ?? DateTime.now(), pageCount: pages.length);
    _documents = [document, ..._documents.where((item) => item.id != id)];
    await _writeIndex();
    notifyListeners();
    return document;
  }

  Future<void> rename(String id, String title) async {
    _documents = [
      for (final item in _documents)
        item.id == id
            ? LocalDocument(id: id, title: title, savedAt: item.savedAt, pageCount: item.pageCount)
            : item,
    ];
    await _writeIndex();
    notifyListeners();
  }

  Future<void> remove(String id) async {
    if (await folder(id).exists()) await folder(id).delete(recursive: true);
    _documents = _documents.where((item) => item.id != id).toList();
    await _writeIndex();
    notifyListeners();
  }

  /// Removes every phone-only document (used on sign-out).
  Future<void> clear() async {
    if (await _root.exists()) await _root.delete(recursive: true);
    _documents = const [];
    notifyListeners();
  }

  Future<void> _writeIndex() async {
    await _root.create(recursive: true);
    await _index.writeAsString(jsonEncode([for (final item in _documents) item.toJson()]), flush: true);
  }
}
