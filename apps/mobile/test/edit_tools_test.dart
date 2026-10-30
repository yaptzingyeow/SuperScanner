import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:arks_scanner/api/api_client.dart';
import 'package:arks_scanner/api/edit_api.dart';
import 'package:arks_scanner/services/document_search.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

class _Tokens implements TokenSource {
  @override
  Future<String> idToken() async => 'id';

  @override
  Future<String> appCheckToken() async => 'ac';
}

Map<String, dynamic> _word(String id, String text, double x) => {
      'id': id,
      'kind': 'Word',
      'text': text,
      'confidence': .9,
      'textType': 'Printed',
      'readingOrder': 0,
      'polygon': [
        {'x': x, 'y': .1}, {'x': x + .05, 'y': .1}, {'x': x + .05, 'y': .12}, {'x': x, 'y': .12},
      ],
      'children': [],
    };

PageOcr _ocr(List<Map<String, dynamic>> words) => PageOcr.fromJson({
      'state': 'Ready',
      'resultId': 'r1',
      'canRetry': false,
      'elements': [
        {'id': 'b', 'kind': 'Block', 'text': '', 'polygon': [], 'children': [
          {'id': 'l', 'kind': 'Line', 'text': '', 'polygon': [], 'children': words},
        ]},
      ],
    });

void main() {
  test('OCR elements flatten to words with normalized boxes and their line', () {
    final ocr = _ocr([_word('w1', 'Hello', .1), _word('w2', 'World', .2)]);
    expect(ocr.words.map((w) => w.text), ['Hello', 'World']);
    expect(ocr.words.first.lineId, 'l');
    expect(ocr.words.first.box.x, closeTo(.1, 1e-9));
    expect(ocr.words.first.box.width, closeTo(.05, 1e-9));
  });

  test('search matches across words, ignoring case and accents, like the web', () {
    final pages = [
      ('p1', _ocr([_word('a', 'Total', .1), _word('b', 'Amount', .2), _word('c', 'Due', .3)])),
      ('p2', _ocr([_word('d', 'CAFÉ', .1), _word('e', 'receipt', .2)])),
    ];
    final hits = DocumentSearch.find('amount due', pages);
    expect(hits.single.pageId, 'p1');
    expect(hits.single.wordIds, ['b', 'c']);
    expect(DocumentSearch.find('cafe', pages).single.wordIds, ['d']);
    expect(DocumentSearch.find('a', pages), isEmpty);
  });

  test('eraser source revision comes from previewRevision', () {
    expect(repairSource('crop-3'), (revisionId: null, cropRevision: 3));
    expect(repairSource('0123456789abcdef0123456789abcdef').revisionId, '01234567-89ab-cdef-0123-456789abcdef');
  });

  test('erase strokes are rounded to 4 decimals like the web', () {
    final stroke = EraseStroke(.0123456)..points.add([.123456, .654321]);
    expect(stroke.toJson(), {'radius': .0123, 'points': [[.1235, .6543]]});
  });

  test('page boxes stay inside the page', () {
    final box = const PageBox(.95, -.1, .2, .1).clamp();
    expect(box.right, lessThanOrEqualTo(1));
    expect(box.y, 0);
  });

  test('a new mark is sent with an idempotency key and the server field names', () async {
    late http.Request sent;
    final client = MockClient((request) async {
      sent = request;
      return http.Response(jsonEncode({
        'id': 'm1', 'pageId': 'p', 'kind': 'Check', 'box': {'x': .1, 'y': .1, 'width': .03, 'height': .02},
        'color': '#000000', 'strokeWidth': .08, 'revision': 1,
      }), 201);
    });
    final edits = EditApi(ApiClient(baseUrl: 'https://api.test/api', tokens: _Tokens(), client: client), client: client);
    final mark = await edits.addMark('d', 'p', kind: 'Check', box: const PageBox(.1, .1, .03, .02),
        color: '#000000', strokeWidth: .08);
    expect(mark.id, 'm1');
    expect(sent.url.path, '/api/documents/d/pages/p/marks');
    expect(sent.headers['Idempotency-Key'], matches(RegExp(r'^[0-9a-f]{8}-[0-9a-f]{4}-4')));
    expect(jsonDecode(sent.body), {
      'kind': 'Check', 'box': {'x': .1, 'y': .1, 'width': .03, 'height': .02}, 'color': '#000000', 'strokeWidth': .08,
    });
  });

  test('add-text requests use the empty OCR id and no words', () async {
    late http.Request sent;
    final client = MockClient((request) async {
      sent = request;
      return http.Response.bytes(Uint8List.fromList([137, 80, 78, 71]), 200);
    });
    final edits = EditApi(ApiClient(baseUrl: 'https://api.test/api', tokens: _Tokens(), client: client), client: client);
    await edits.previewTextEdit('d', 'p', ocrResultId: null, expectedRevisionId: null, wordIds: const [], text: 'Hi',
        box: const PageBox(.1, .1, .3, .05),
        style: const TextStyleSpec(fontId: 'noto-sans', fontVersion: 'archive-main-regular', fontSize: .02));
    final body = jsonDecode(sent.body) as Map<String, dynamic>;
    expect(body['ocrResultId'], '00000000-0000-0000-0000-000000000000');
    expect(body['wordIds'], isEmpty);
    expect(body.containsKey('idempotencyKey'), isFalse);
    expect((body['style'] as Map)['alignment'], 0);
  });

  test('all four languages have the same keys and placeholders', () {
    Map<String, dynamic> read(String locale) =>
        jsonDecode(File('lib/l10n/app_$locale.arb').readAsStringSync()) as Map<String, dynamic>;
    final en = read('en');
    final keys = en.keys.where((key) => !key.startsWith('@')).toSet();
    for (final locale in ['ms', 'zh', 'ar']) {
      final other = read(locale);
      expect(other.keys.where((key) => !key.startsWith('@')).toSet(), keys, reason: locale);
      for (final key in keys) {
        for (final placeholder in (en['@$key']?['placeholders'] as Map? ?? const {}).keys) {
          expect(other[key], contains('{$placeholder}'), reason: '$locale.$key');
        }
      }
    }
  });
}
