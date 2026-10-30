import 'dart:convert';
import 'dart:typed_data';

import 'package:http/http.dart' as http;

import 'models.dart';

/// Supplies the Firebase ID token and App Check token for each request.
abstract interface class TokenSource {
  Future<String> idToken();
  Future<String> appCheckToken();
}

/// A failed API call. [code] is the server's safe error code when it sent one (e.g. `plan_limit_reached`).
class ApiException implements Exception {
  const ApiException(this.statusCode, [this.code]);

  final int statusCode;
  final String? code;

  @override
  String toString() => 'ApiException($statusCode${code == null ? '' : ', $code'})';
}

/// Calls the Arks Scanner API with the same headers as the web app's security interceptor.
class ApiClient {
  ApiClient({required String baseUrl, required TokenSource tokens, http.Client? client, String? timeZone})
      : _baseUrl = baseUrl.replaceAll(RegExp(r'/+$'), ''),
        _tokens = tokens, // ignore: prefer_initializing_formals
        _http = client ?? http.Client(),
        _timeZone = timeZone; // ignore: prefer_initializing_formals

  final String _baseUrl;
  final TokenSource _tokens;
  final http.Client _http;
  final String? _timeZone;

  Uri url(String path) => Uri.parse('$_baseUrl$path');

  Future<Map<String, String>> headers({String? contentType}) async {
    final tokens = await Future.wait([_tokens.idToken(), _tokens.appCheckToken()]);
    return {
      'Authorization': 'Bearer ${tokens[0]}',
      'X-Firebase-AppCheck': tokens[1],
      'X-Time-Zone': ?_timeZone,
      'Content-Type': ?contentType,
    };
  }

  Future<dynamic> getJson(String path) async => _decode(await _http.get(url(path), headers: await headers()));

  Future<dynamic> postJson(String path, [Object? body]) async => _decode(await _http.post(url(path),
      headers: await headers(contentType: 'application/json'), body: body == null ? null : jsonEncode(body)));

  Future<dynamic> putJson(String path, Object body) async => _decode(await _http.put(url(path),
      headers: await headers(contentType: 'application/json'), body: jsonEncode(body)));

  Future<dynamic> patchJson(String path, Object body) async => _decode(await _http.patch(url(path),
      headers: await headers(contentType: 'application/json'), body: jsonEncode(body)));

  Future<void> delete(String path) async => _decode(await _http.delete(url(path), headers: await headers()));

  Future<void> deleteWithBody(String path, Object body) async => _decode(await _http.delete(url(path),
      headers: await headers(contentType: 'application/json'), body: jsonEncode(body)));

  Future<Uint8List> getBytes(String path) async {
    final response = await _http.get(url(path), headers: await headers());
    _check(response);
    return response.bodyBytes;
  }

  /// PUT to a pre-signed storage URL. No API headers: the URL itself carries the permission.
  Future<void> putToStorage(Uri putUrl, Uint8List bytes, String mediaType) async {
    final response = await _http.put(putUrl, headers: {'Content-Type': mediaType}, body: bytes);
    if (response.statusCode < 200 || response.statusCode >= 300) throw ApiException(response.statusCode, 'upload_failed');
  }

  dynamic _decode(http.Response response) {
    _check(response);
    if (response.body.isEmpty) return null;
    return jsonDecode(response.body);
  }

  void _check(http.Response response) {
    if (response.statusCode >= 200 && response.statusCode < 300) return;
    String? code;
    try {
      final body = jsonDecode(response.body);
      if (body is Map<String, dynamic>) code = (body['code'] ?? body['errorCode']) as String?;
    } catch (_) {
      // Not JSON: keep the status code only.
    }
    throw ApiException(response.statusCode, code);
  }
}

/// Typed calls used by the app (mirrors apps/web/src/app/documents/documents-api.service.ts).
class DocumentsApi {
  DocumentsApi(this._api);

  final ApiClient _api;

  ApiClient get client => _api;

  Future<List<DocumentSummary>> listDocuments() async => [
        for (final item in await _api.getJson('/documents') as List)
          DocumentSummary.fromJson(item as Map<String, dynamic>),
      ];

  Future<DocumentSummary> createDocument(String title) async =>
      DocumentSummary.fromJson(await _api.postJson('/documents', {'title': title}) as Map<String, dynamic>);

  Future<DocumentDetail> getDocument(String id) async =>
      DocumentDetail.fromJson(await _api.getJson('/documents/$id') as Map<String, dynamic>);

  Future<void> renameDocument(String id, String title) => _api.patchJson('/documents/$id', {'title': title});

  /// [savedToDevice]: the user kept the document on the phone, so the server copy is purged soon.
  Future<void> deleteDocument(String id, {bool savedToDevice = false}) =>
      _api.delete('/documents/$id${savedToDevice ? '?savedToDevice=true' : ''}');

  Future<UploadIntent> createUploadIntent(String documentId,
          {required String fileName, required String mediaType, required int sizeBytes, required String sha256Hex}) async =>
      UploadIntent.fromJson(await _api.postJson('/documents/$documentId/uploads', {
        'fileName': fileName,
        'mediaType': mediaType,
        'sizeBytes': sizeBytes,
        'sha256Hex': sha256Hex,
      }) as Map<String, dynamic>);

  Future<UploadStatus> completeUpload(String documentId, String uploadId) async => UploadStatus.fromJson(
      await _api.postJson('/documents/$documentId/uploads/$uploadId/complete') as Map<String, dynamic>);

  Future<UploadStatus> getUploadStatus(String documentId, String uploadId) async =>
      UploadStatus.fromJson(await _api.getJson('/documents/$documentId/uploads/$uploadId') as Map<String, dynamic>);

  Future<void> reorderPages(String documentId, int expectedPageOrderRevision, List<String> pageIds) =>
      _api.putJson('/documents/$documentId/page-order',
          {'expectedPageOrderRevision': expectedPageOrderRevision, 'pageIds': pageIds});

  Future<void> removePage(String documentId, String pageId) => _api.delete('/documents/$documentId/pages/$pageId');

  /// [asset] is `preview`, `thumbnail`, `original` or `crop-source`.
  Future<Uint8List> pageImage(String documentId, String pageId, String asset) =>
      _api.getBytes('/documents/$documentId/pages/$pageId/$asset');

  Future<CropState> getCrop(String documentId, String pageId) async =>
      CropState.fromJson(await _api.getJson('/documents/$documentId/pages/$pageId/crop') as Map<String, dynamic>);

  Future<CropState> applyCrop(String documentId, String pageId,
          {required int revision, required List<CropPoint> points, required String filter, required int rotation}) async =>
      CropState.fromJson(await _api.postJson('/documents/$documentId/pages/$pageId/crop/apply', {
        'revision': revision,
        'points': [for (final point in points) point.toJson()],
        'filter': filter,
        'rotation': rotation,
      }) as Map<String, dynamic>);

  Future<CropState> detectCrop(String documentId, String pageId, int revision) async => CropState.fromJson(
      await _api.postJson('/documents/$documentId/pages/$pageId/crop/detect', {'revision': revision})
          as Map<String, dynamic>);

  Future<DocumentExport> createExport(String documentId, {bool searchable = false}) async => DocumentExport.fromJson(
      await _api.postJson('/documents/$documentId/exports', {'pageLayout': 'Original', 'includeSearchableText': searchable})
          as Map<String, dynamic>);

  Future<DocumentExport> getExport(String documentId, String exportId) async => DocumentExport.fromJson(
      await _api.getJson('/documents/$documentId/exports/$exportId') as Map<String, dynamic>);

  Future<Uint8List> downloadExport(String documentId, String exportId) =>
      _api.getBytes('/documents/$documentId/exports/$exportId/download');

  Future<PlanInfo> getPlan() async => PlanInfo.fromJson(await _api.getJson('/me/plan') as Map<String, dynamic>);

  Future<void> recordConsent(String version) => _api.postJson('/me/consent', {'version': version});
}
