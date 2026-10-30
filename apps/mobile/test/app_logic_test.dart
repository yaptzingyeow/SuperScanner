import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:arks_scanner/api/api_client.dart';
import 'package:arks_scanner/api/models.dart';
import 'package:arks_scanner/services/export_service.dart';
import 'package:arks_scanner/services/local_store.dart';
import 'package:arks_scanner/l10n/app_localizations.dart';
import 'package:arks_scanner/services/upload_service.dart';
import 'package:arks_scanner/ui/common.dart';
import 'package:crypto/crypto.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

class _Tokens implements TokenSource {
  @override
  Future<String> idToken() async => 'id-token';

  @override
  Future<String> appCheckToken() async => 'app-check-token';
}

/// A tiny fake API: records requests and answers from [routes] ("METHOD path" → handler).
class _FakeApi {
  _FakeApi(this.routes);

  final Map<String, http.Response Function(http.Request request)> routes;
  final requests = <http.Request>[];

  late final client = MockClient((request) async {
    requests.add(request);
    final key = '${request.method} ${request.url.path}';
    final handler = routes[key] ?? routes['${request.method} ${request.url.host}'];
    return handler?.call(request) ?? http.Response('{"code":"not_found"}', 404);
  });

  DocumentsApi get api => DocumentsApi(ApiClient(baseUrl: 'https://api.test/api/', tokens: _Tokens(), client: client));
}

http.Response _json(Object body, [int status = 200]) =>
    http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json'});

Map<String, dynamic> _detail({required List<Map<String, dynamic>> pages, List<Map<String, dynamic>> imports = const []}) => {
      'id': 'doc-1',
      'title': 'Receipt',
      'status': 'Ready',
      'revision': 3,
      'pageOrderRevision': 1,
      'pages': pages,
      'imports': imports,
    };

Map<String, dynamic> _page(String id, int position, {String state = 'Ready', String cropStatus = 'Ready'}) => {
      'id': id,
      'position': position,
      'pageNumber': position + 1,
      'state': state,
      'hasPreview': state == 'Ready',
      'canCrop': true,
      'cropStatus': cropStatus,
      'previewRevision': 'r1',
      'filter': 'Magic',
      'appliedFilter': 'Magic',
    };

void main() {
  group('ApiClient', () {
    test('sends the sign-in and App Check headers like the web interceptor', () async {
      final fake = _FakeApi({'GET /api/documents': (_) => _json([])});
      await fake.api.listDocuments();
      final request = fake.requests.single;
      expect(request.url.toString(), 'https://api.test/api/documents');
      expect(request.headers['Authorization'], 'Bearer id-token');
      expect(request.headers['X-Firebase-AppCheck'], 'app-check-token');
    });

    test('turns a failed response into an ApiException with the server code', () async {
      final fake = _FakeApi({'POST /api/documents': (_) => _json({'code': 'plan_limit_reached'}, 429)});
      await expectLater(
        fake.api.createDocument('A'),
        throwsA(isA<ApiException>()
            .having((error) => error.statusCode, 'status', 429)
            .having((error) => error.code, 'code', 'plan_limit_reached')),
      );
    });

    test('marks a phone-saved delete so the server skips the recycle bin', () async {
      final fake = _FakeApi({'DELETE /api/documents/doc-1': (_) => http.Response('', 204)});
      await fake.api.deleteDocument('doc-1', savedToDevice: true);
      expect(fake.requests.single.url.queryParameters['savedToDevice'], 'true');
    });
  });

  group('UploadService', () {
    test('creates an intent with the file hash, PUTs to storage without API headers, then completes', () async {
      final bytes = Uint8List.fromList(List.filled(32, 7));
      final fake = _FakeApi({
        'POST /api/documents/doc-1/uploads': (_) => _json({
              'uploadId': 'up-1',
              'pageId': 'p-1',
              'putUrl': 'https://storage.test/put?sig=1',
              'expiresAt': DateTime.now().add(const Duration(minutes: 5)).toUtc().toIso8601String(),
            }),
        'PUT storage.test': (_) => http.Response('', 200),
        'POST /api/documents/doc-1/uploads/up-1/complete': (_) => _json({
              'uploadId': 'up-1', 'state': 'PendingValidation', 'discoveredPageCount': 0,
              'createdPageCount': 0, 'failedPageCount': 0,
            }),
      });

      final id = await UploadService(fake.api)
          .upload('doc-1', UploadFile(name: 'scan-1.jpg', mediaType: 'image/jpeg', bytes: bytes));

      expect(id, 'up-1');
      final intent = jsonDecode(fake.requests[0].body) as Map<String, dynamic>;
      expect(intent['sha256Hex'], sha256.convert(bytes).toString());
      expect(intent['sizeBytes'], 32);
      final put = fake.requests[1];
      expect(put.method, 'PUT');
      expect(put.headers['Content-Type'], 'image/jpeg');
      expect(put.headers.containsKey('Authorization'), isFalse);
      expect(put.bodyBytes, bytes);
      expect(fake.requests[2].url.path, '/api/documents/doc-1/uploads/up-1/complete');
    });

    test('rejects unsupported, empty and oversized files before contacting the server', () {
      expect(() => UploadService.validate(UploadFile(name: 'a.gif', mediaType: 'image/gif', bytes: Uint8List(1))),
          throwsA(isA<UploadException>().having((e) => e.code, 'code', 'unsupported_type')));
      expect(() => UploadService.validate(UploadFile(name: 'a.jpg', mediaType: 'image/jpeg', bytes: Uint8List(0))),
          throwsA(isA<UploadException>().having((e) => e.code, 'code', 'empty_file')));
      expect(
          () => UploadService.validate(
              UploadFile(name: 'a.pdf', mediaType: 'application/pdf', bytes: Uint8List(UploadService.maxBytes + 1))),
          throwsA(isA<UploadException>().having((e) => e.code, 'code', 'file_too_large')));
    });

    test('names scans like CamScanner and maps file types', () {
      expect(UploadService.defaultTitle(DateTime(2026, 10, 29, 15, 4)), 'Scan 29 Oct 2026 15:04');
      expect(UploadService.mediaTypeFor('IMG_1.HEIC'), 'image/heic');
      expect(UploadService.mediaTypeFor('a.pdf'), 'application/pdf');
      expect(UploadService.mediaTypeFor('a.docx'), isNull);
    });
  });

  testWidgets('error messages say what went wrong and carry a code to report', (tester) async {
    late BuildContext context;
    await tester.pumpWidget(MaterialApp(
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: Builder(builder: (c) {
        context = c;
        return const SizedBox();
      }),
    ));
    expect(friendlyError(context, const ApiException(422, 'text_selection_invalid')),
        'Choose neighbouring words on one line.\nError code: text_selection_invalid · HTTP 422');
    expect(friendlyError(context, const ApiException(404)), startsWith('This document or page no longer exists.'));
    expect(friendlyError(context, const SocketException('down')),
        "Can't reach the server. Check your internet connection and try again.\nError code: SocketException");
    expect(friendlyError(context, const ApiException(503)), endsWith('Error code: HTTP 503'));
  });

  group('models', () {
    test('a document is busy while pages or imports are still processing', () {
      final ready = DocumentDetail.fromJson(_detail(pages: [_page('a', 0)]));
      final cropping = DocumentDetail.fromJson(_detail(pages: [_page('a', 0, cropStatus: 'Processing')]));
      final importing = DocumentDetail.fromJson(_detail(pages: [_page('a', 0)], imports: [
        {'uploadId': 'u', 'state': 'PendingValidation', 'discoveredPageCount': 0, 'createdPageCount': 0,
          'failedPageCount': 0},
      ]));
      expect(ready.isBusy, isFalse);
      expect(cropping.isBusy, isTrue);
      expect(importing.isBusy, isTrue);
    });

    test('finished imports ("Completed" on the document endpoint) and pages awaiting a crop do not block export', () {
      Map<String, dynamic> import(String state) => {'uploadId': 'u', 'state': state, 'discoveredPageCount': 1,
          'createdPageCount': 1, 'failedPageCount': 0};
      expect(DocumentDetail.fromJson(_detail(pages: [_page('a', 0)], imports: [import('Completed')])).isBusy, isFalse);
      expect(DocumentDetail.fromJson(_detail(pages: [_page('a', 0)], imports: [import('Failed')])).isBusy, isFalse);
      expect(DocumentDetail.fromJson(_detail(pages: [_page('a', 0, state: 'NeedsCrop')])).isBusy, isFalse);
    });

    test('pages are ordered by position', () {
      final document = DocumentDetail.fromJson(_detail(pages: [_page('b', 1), _page('a', 0)]));
      expect(document.pages.map((page) => page.id), ['a', 'b']);
    });

    test('privacy consent is needed when the notice version changed', () {
      expect(PlanInfo.fromJson({'plan': 'Free', 'privacyConsentVersion': null, 'currentPrivacyVersion': '2026-10'})
          .needsConsent, isTrue);
      expect(PlanInfo.fromJson({'plan': 'Pro', 'privacyConsentVersion': '2026-10', 'currentPrivacyVersion': '2026-10'})
          .needsConsent, isFalse);
    });
  });

  group('ExportService.saveToPhone', () {
    late Directory temp;

    setUp(() async => temp = await Directory.systemTemp.createTemp('arks-test'));
    tearDown(() async => temp.delete(recursive: true));

    _FakeApi exportApi() => _FakeApi({
          'POST /api/documents/doc-1/exports': (_) => _json({'id': 'ex-1', 'state': 'Queued'}),
          'GET /api/documents/doc-1/exports/ex-1': (_) => _json({'id': 'ex-1', 'state': 'Ready'}),
          'GET /api/documents/doc-1/exports/ex-1/download': (_) => http.Response.bytes([37, 80, 68, 70], 200),
          'GET /api/documents/doc-1/pages/a/preview': (_) => http.Response.bytes([1, 2, 3], 200),
          'DELETE /api/documents/doc-1': (_) => http.Response('', 204),
        });

    test('Free: keeps the PDF and pages on the phone and removes the cloud copy', () async {
      final fake = exportApi();
      final store = LocalStore(Directory('${temp.path}/docs'));
      final document = DocumentDetail.fromJson(_detail(pages: [_page('a', 0)]));

      final saved = await ExportService(fake.api, store, pollInterval: Duration.zero)
          .saveToPhone(document, removeFromServer: true);

      expect(saved.pageCount, 1);
      expect(await store.pdfFile('doc-1').readAsBytes(), [37, 80, 68, 70]);
      expect(await store.pageFile('doc-1', 1).readAsBytes(), [1, 2, 3]);
      final delete = fake.requests.singleWhere((request) => request.method == 'DELETE');
      expect(delete.url.queryParameters['savedToDevice'], 'true');

      final reloaded = LocalStore(Directory('${temp.path}/docs'));
      await reloaded.load();
      expect(reloaded.documents.single.title, 'Receipt');
    });

    test('Pro: saves a copy and keeps the cloud document', () async {
      final fake = exportApi();
      final store = LocalStore(Directory('${temp.path}/docs'));
      await ExportService(fake.api, store, pollInterval: Duration.zero)
          .saveToPhone(DocumentDetail.fromJson(_detail(pages: [_page('a', 0)])), removeFromServer: false);
      expect(fake.requests.where((request) => request.method == 'DELETE'), isEmpty);
    });

    test('sign-out clears every phone-only document', () async {
      final store = LocalStore(Directory('${temp.path}/docs'));
      await store.save(id: 'x', title: 'T', pdf: Uint8List(1), pages: [Uint8List(1)]);
      await store.clear();
      expect(store.documents, isEmpty);
      expect(await store.folder('x').exists(), isFalse);
    });
  });
}
