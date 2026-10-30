import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:cunning_document_scanner/cunning_document_scanner.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/api_client.dart';
import '../l10n/app_localizations.dart';
import '../services/upload_service.dart';

/// Brand colours from the web app (apps/web/src/styles.scss).
abstract final class Brand {
  static const green = Color(0xFF176047);
  static const greenSoft = Color(0xFFEAF3ED);
  static const danger = Color(0xFFAC3D39);
}

/// Short access to the translated strings: `context.l.scan`.
extension L10nContext on BuildContext {
  AppLocalizations get l => AppLocalizations.of(this);
}

/// Plain-language message for an error, followed by a short code (server code and HTTP status, or
/// the error type) that users can quote when reporting it. Never includes tokens, URLs or file contents.
String friendlyError(BuildContext context, Object error) {
  final l = context.l;
  final code = switch (error) {
    ApiException(:final code) => code,
    UploadException(:final code) => code,
    _ => null,
  };
  final status = error is ApiException ? error.statusCode : null;
  final offline = error is SocketException || error is HttpException || error is HandshakeException ||
      error is TimeoutException || error.runtimeType.toString() == 'ClientException';
  final message = offline ? l.errNoConnection : _messageFor(l, code, status);
  final detail = [?code, if (status != null) 'HTTP $status', if (code == null && status == null) error.runtimeType.toString()]
      .join(' · ');
  return '$message\n${l.errorCode(detail)}';
}

String _messageFor(AppLocalizations l, String? code, int? status) {
  return switch (code) {
    'plan_limit_reached' => l.errLimit,
    'unsupported_type' => l.errType,
    'file_too_large' => l.errTooLarge,
    'empty_file' => l.errEmpty,
    'upload_failed' => l.errUpload,
    'export_timeout' => l.errExportTimeout,
    'signed_out' => l.errSignedOut,
    'app_check_unavailable' => l.errAppCheck,
    'text_edit_overflow' => l.errOverflow,
    'text_edit_unsafe_background' => l.errUnsafeBackground,
    'text_edit_placement_overlap' => l.errOverlap,
    'text_selection_stale' || 'text_revision_conflict' || 'mark_revision_conflict' || 'signature_revision_conflict' =>
      l.errStale,
    'text_selection_invalid' => l.errSelectionInvalid,
    'export_watermark_invalid' => l.errWatermark,
    'text_selection_unsupported' => l.errSelectionUnsupported,
    'text_edit_font_unsupported' || 'text_edit_font_unavailable' => l.errFontUnsupported,
    'text_edit_disabled' => l.errTextDisabled,
    'ocr_disabled' => l.errOcrDisabled,
    'page_not_ready' => l.errPageNotReady,
    'repair_failed' => l.errRepairFailed,
    'signature_no_ink' => l.errSignatureNoInk,
    'signature_invalid_image' || 'signature_too_large' || 'signature_png_required' => l.errSignatureImage,
    _ => switch (status) {
        401 => l.errSignedOut,
        403 => l.errForbidden,
        404 => l.errNotFound,
        409 => l.errStale,
        413 => l.errTooLarge,
        422 || 400 => l.errRejected,
        429 => l.errTooMany,
        final int s when s >= 500 => l.errServer,
        _ => l.errGeneric,
      },
  };
}

/// Translated name of a scan look (server filter id).
String lookLabel(BuildContext context, String? id) {
  final l = context.l;
  return switch (id) {
    'Magic' => l.lookMagic,
    'Original' => l.lookOriginal,
    'Document' => l.lookDocument,
    'Bright' => l.lookBright,
    'RemoveShadows' => l.lookRemoveShadows,
    'Grayscale' => l.lookGrayscale,
    'BlackAndWhite' => l.lookBlackAndWhite,
    _ => l.ready,
  };
}

void showMessage(BuildContext context, String message) =>
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));

/// Opens the phone's document scanner (live edges, auto-capture, several pages).
Future<List<UploadFile>> scanPages() async {
  final paths = await CunningDocumentScanner.getPictures() ?? const <String>[];
  final files = <UploadFile>[];
  for (var index = 0; index < paths.length; index++) {
    final bytes = await XFileReader.read(paths[index]);
    final type = UploadService.mediaTypeFor(paths[index]) ?? 'image/jpeg';
    files.add(UploadFile(name: 'scan-${index + 1}.${type == 'image/png' ? 'png' : 'jpg'}', mediaType: type, bytes: bytes));
  }
  return files;
}

/// Lets the user pick photos or PDFs from the phone.
Future<List<UploadFile>> pickFiles() async {
  final picked = await FilePicker.pickFiles(
      type: FileType.custom, allowedExtensions: const ['jpg', 'jpeg', 'png', 'heic', 'heif', 'pdf']);
  final files = <UploadFile>[];
  for (final file in picked) {
    final type = UploadService.mediaTypeFor(file.name);
    if (type == null) throw const UploadException('unsupported_type');
    files.add(UploadFile(name: file.name, mediaType: type, bytes: await file.readAsBytes()));
  }
  return files;
}

/// Reads a local file path returned by the scanner.
abstract final class XFileReader {
  static Future<Uint8List> read(String path) => File(path).readAsBytes();
}

/// A page image loaded from the API (which needs the sign-in headers, so Image.network cannot be used).
class ApiImage extends StatefulWidget {
  const ApiImage({super.key, required this.documentId, required this.pageId, required this.asset,
      required this.revision, this.fit = BoxFit.contain});

  final String documentId;
  final String pageId;
  final String asset;

  /// Changes whenever the server image changes, so the widget reloads.
  final String revision;
  final BoxFit fit;

  @override
  State<ApiImage> createState() => _ApiImageState();
}

class _ApiImageState extends State<ApiImage> {
  static final _cache = <String, Future<Uint8List>>{};
  late Future<Uint8List> _bytes;

  String get _key => '${widget.documentId}/${widget.pageId}/${widget.asset}/${widget.revision}';

  @override
  void initState() {
    super.initState();
    _bytes = _load();
  }

  @override
  void didUpdateWidget(ApiImage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.revision != widget.revision || oldWidget.pageId != widget.pageId) _bytes = _load();
  }

  Future<Uint8List> _load() {
    if (_cache.length > 80) _cache.clear();
    return _cache.putIfAbsent(_key, () {
      final api = context.read<DocumentsApi>();
      return api.pageImage(widget.documentId, widget.pageId, widget.asset)
        ..catchError((Object _) {
          _cache.remove(_key);
          return Uint8List(0);
        });
    });
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<Uint8List>(
        future: _bytes,
        builder: (context, snapshot) {
          final bytes = snapshot.data;
          if (snapshot.hasError) return const Center(child: Icon(Icons.broken_image_outlined));
          if (bytes == null) return const Center(child: CircularProgressIndicator(strokeWidth: 2));
          return Image.memory(bytes, fit: widget.fit, gaplessPlayback: true);
        },
      );
}
