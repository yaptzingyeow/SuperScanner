import 'dart:typed_data';

import 'package:crypto/crypto.dart';

import '../api/api_client.dart';

/// One picked or scanned file, ready to upload.
class UploadFile {
  const UploadFile({required this.name, required this.mediaType, required this.bytes});

  final String name;
  final String mediaType;
  final Uint8List bytes;
}

/// Thrown when a file cannot be sent; [code] matches the web upload flow's codes.
class UploadException implements Exception {
  const UploadException(this.code);

  final String code;

  @override
  String toString() => 'UploadException($code)';
}

/// Uploads files the same way the web app does: intent → PUT to storage → complete.
/// The server then validates, virus-scans and splits each file into pages in the background.
class UploadService {
  UploadService(this._documents);

  final DocumentsApi _documents;

  static const maxBytes = 25 * 1024 * 1024;
  static const supportedTypes = {'image/jpeg', 'image/png', 'image/heic', 'image/heif', 'application/pdf'};

  /// Media type from a file name; null when the app cannot send it.
  static String? mediaTypeFor(String fileName) {
    final name = fileName.toLowerCase();
    if (name.endsWith('.jpg') || name.endsWith('.jpeg')) return 'image/jpeg';
    if (name.endsWith('.png')) return 'image/png';
    if (name.endsWith('.heic')) return 'image/heic';
    if (name.endsWith('.heif')) return 'image/heif';
    if (name.endsWith('.pdf')) return 'application/pdf';
    return null;
  }

  static void validate(UploadFile file) {
    if (!supportedTypes.contains(file.mediaType)) throw const UploadException('unsupported_type');
    if (file.bytes.isEmpty) throw const UploadException('empty_file');
    if (file.bytes.length > maxBytes) throw const UploadException('file_too_large');
  }

  /// Sends [file] into [documentId] and returns its upload ID. Page processing continues on the server.
  Future<String> upload(String documentId, UploadFile file) async {
    validate(file);
    final intent = await _documents.createUploadIntent(documentId,
        fileName: file.name,
        mediaType: file.mediaType,
        sizeBytes: file.bytes.length,
        sha256Hex: sha256.convert(file.bytes).toString());
    if (!intent.expiresAt.isAfter(DateTime.now())) throw const UploadException('intent_expired');
    try {
      await _documents.client.putToStorage(intent.putUrl, file.bytes, file.mediaType);
    } on ApiException {
      throw const UploadException('upload_failed');
    }
    final status = await _documents.completeUpload(documentId, intent.uploadId);
    if (status.isRejected) throw UploadException(status.errorCode ?? 'rejected');
    return intent.uploadId;
  }

  /// Creates a document named [title] and uploads every file into it.
  Future<String> uploadNewDocument(String title, List<UploadFile> files) async {
    final document = await _documents.createDocument(title);
    for (final file in files) {
      await upload(document.id, file);
    }
    return document.id;
  }

  /// Default document name, e.g. "Scan 29 Oct 2026 15:40".
  static String defaultTitle(DateTime now) {
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    String two(int value) => value.toString().padLeft(2, '0');
    return 'Scan ${now.day} ${months[now.month - 1]} ${now.year} ${two(now.hour)}:${two(now.minute)}';
  }
}
