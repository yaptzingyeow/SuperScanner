import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import '../api/api_client.dart';
import '../api/models.dart';
import 'local_store.dart';

/// Builds PDFs on the server and keeps finished documents on the phone.
class ExportService {
  ExportService(this._documents, this._store, {Duration pollInterval = const Duration(seconds: 1), File? watermarkFile})
      : _pollInterval = pollInterval, // ignore: prefer_initializing_formals
        _watermarkFile = watermarkFile; // ignore: prefer_initializing_formals

  final DocumentsApi _documents;
  final LocalStore _store;
  final Duration _pollInterval;
  final File? _watermarkFile;

  /// Asks the server for a PDF of [documentId] (with [watermark] on every page) and returns its bytes once ready.
  Future<Uint8List> buildPdf(String documentId,
      {bool searchable = false, WatermarkSettings? watermark, Duration timeout = const Duration(minutes: 3)}) async {
    var export = await _documents.createExport(documentId, searchable: searchable, watermark: watermark);
    final deadline = DateTime.now().add(timeout);
    while (!export.isReady) {
      if (export.isFailed) throw ApiException(500, export.failureCode ?? 'export_failed');
      if (DateTime.now().isAfter(deadline)) throw const ApiException(504, 'export_timeout');
      await Future<void>.delayed(_pollInterval);
      export = await _documents.getExport(documentId, export.id);
    }
    return _documents.downloadExport(documentId, export.id);
  }

  /// Saves [document] on this phone (PDF + processed page images).
  /// When [removeFromServer] is true (Free plan), the server copy is deleted and purged on the next run.
  Future<LocalDocument> saveToPhone(DocumentDetail document,
      {required bool removeFromServer, WatermarkSettings? watermark}) async {
    final pdf = await buildPdf(document.id, watermark: watermark);
    final pages = <Uint8List>[
      for (final page in document.pages.where((page) => page.hasPreview))
        await _documents.pageImage(document.id, page.id, 'preview'),
    ];
    final saved = await _store.save(id: document.id, title: document.title, pdf: pdf, pages: pages);
    if (removeFromServer) await _documents.deleteDocument(document.id, savedToDevice: true);
    return saved;
  }

  /// The last watermark the user exported with, and whether it was switched on.
  Future<({WatermarkSettings settings, bool enabled})> lastWatermark() async {
    try {
      final json = jsonDecode(await _watermarkFile!.readAsString()) as Map<String, dynamic>;
      return (settings: WatermarkSettings.fromJson(json), enabled: json['enabled'] == true);
    } catch (_) {
      return (settings: const WatermarkSettings(), enabled: false);
    }
  }

  Future<void> rememberWatermark(WatermarkSettings settings, {required bool enabled}) async {
    final file = _watermarkFile;
    if (file == null) return;
    await file.parent.create(recursive: true);
    await file.writeAsString(jsonEncode({...settings.toJson(), 'enabled': enabled}));
  }
}
