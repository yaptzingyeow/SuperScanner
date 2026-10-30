import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:http/http.dart' as http;

import 'api_client.dart';

/// Normalized (0..1) box on the page image.
class PageBox {
  const PageBox(this.x, this.y, this.width, this.height);

  final double x;
  final double y;
  final double width;
  final double height;

  double get right => x + width;
  double get bottom => y + height;

  /// Keeps the box inside the page (server rule: x,y >= 0 and x+width, y+height <= 1).
  PageBox clamp() {
    final w = width.clamp(0.005, 1.0);
    final h = height.clamp(0.005, 1.0);
    return PageBox(x.clamp(0.0, 1 - w), y.clamp(0.0, 1 - h), w, h);
  }

  PageBox moved(double dx, double dy) => PageBox(x + dx, y + dy, width, height).clamp();

  Map<String, dynamic> toJson() => {'x': x, 'y': y, 'width': width, 'height': height};

  factory PageBox.fromJson(Map<String, dynamic> json) => PageBox((json['x'] as num).toDouble(),
      (json['y'] as num).toDouble(), (json['width'] as num).toDouble(), (json['height'] as num).toDouble());
}

/// A tick or cross drawn on the page at export time.
class PageMark {
  const PageMark({required this.id, required this.kind, required this.box, required this.color,
      required this.strokeWidth, required this.revision});

  final String id;
  final String kind; // "Check" or "Cross" (case-sensitive on the server)
  final PageBox box;
  final String color;
  final double strokeWidth;
  final int revision;

  factory PageMark.fromJson(Map<String, dynamic> json) => PageMark(
        id: json['id'] as String,
        kind: json['kind'] as String,
        box: PageBox.fromJson(json['box'] as Map<String, dynamic>),
        color: json['color'] as String,
        strokeWidth: (json['strokeWidth'] as num).toDouble(),
        revision: (json['revision'] as num).toInt(),
      );
}

/// A placed signature image, drawn on the page at export time.
class PageSignature {
  const PageSignature({required this.id, required this.box, required this.aspectRatio, required this.revision});

  final String id;
  final PageBox box;
  final double aspectRatio;
  final int revision;

  factory PageSignature.fromJson(Map<String, dynamic> json) => PageSignature(
        id: json['id'] as String,
        box: PageBox.fromJson(json['box'] as Map<String, dynamic>),
        aspectRatio: (json['imageAspectRatio'] as num).toDouble(),
        revision: (json['revision'] as num).toInt(),
      );
}

class OcrWord {
  const OcrWord({required this.id, required this.text, required this.box, required this.lineId});

  final String id;
  final String text;
  final PageBox box;
  final String lineId;
}

/// Recognised text of one page, flattened to words (Block → Line → Word on the server).
class PageOcr {
  const PageOcr({required this.state, required this.canRetry, this.resultId, this.failureCode, this.words = const []});

  final String state; // NotRequested | Queued | Processing | Ready | Failed
  final String? resultId;
  final String? failureCode;
  final bool canRetry;
  final List<OcrWord> words;

  bool get isReady => state == 'Ready';
  bool get isWorking => state == 'Queued' || state == 'Processing';
  bool get canRequest => state == 'NotRequested' || (state == 'Failed' && canRetry);

  factory PageOcr.fromJson(Map<String, dynamic> json) {
    final words = <OcrWord>[];
    void walk(Map<String, dynamic> element, String? lineId) {
      final kind = element['kind'] as String;
      final id = element['id'] as String;
      if (kind == 'Word') {
        final points = [for (final p in element['polygon'] as List) p as Map<String, dynamic>];
        if (points.isNotEmpty) {
          final xs = points.map((p) => (p['x'] as num).toDouble());
          final ys = points.map((p) => (p['y'] as num).toDouble());
          final left = xs.reduce(min), top = ys.reduce(min);
          words.add(OcrWord(id: id, text: element['text'] as String? ?? '', lineId: lineId ?? id,
              box: PageBox(left, top, xs.reduce(max) - left, ys.reduce(max) - top)));
        }
      }
      for (final child in (element['children'] as List? ?? const [])) {
        walk(child as Map<String, dynamic>, kind == 'Line' ? id : lineId);
      }
    }

    for (final element in (json['elements'] as List? ?? const [])) {
      walk(element as Map<String, dynamic>, null);
    }
    return PageOcr(
      state: json['state'] as String,
      resultId: json['resultId'] as String?,
      failureCode: json['failureCode'] as String?,
      canRetry: json['canRetry'] as bool? ?? false,
      words: words,
    );
  }
}

/// A font the text editor may use (from GET /api/text-edit-fonts).
class FontFace {
  const FontFace({required this.catalogueId, required this.version, required this.familyName, required this.category,
      required this.weight, required this.webFamilyName, required this.assetUrl, required this.scripts});

  final String catalogueId;
  final String version;
  final String familyName;
  final String category;
  final int weight;
  final String webFamilyName;
  final String assetUrl;
  final List<String> scripts;

  factory FontFace.fromJson(Map<String, dynamic> json) => FontFace(
        catalogueId: json['catalogueId'] as String,
        version: json['version'] as String,
        familyName: json['familyName'] as String,
        category: json['category'] as String,
        weight: (json['weight'] as num).toInt(),
        webFamilyName: json['webFamilyName'] as String,
        assetUrl: json['webAssetUrl'] as String,
        scripts: [for (final s in (json['scripts'] as List? ?? const [])) s as String],
      );
}

/// Style of replacement text, in the server's units (fontSize = fraction of page height).
class TextStyleSpec {
  const TextStyleSpec({required this.fontId, required this.fontVersion, required this.fontSize, this.weight = 400,
      this.colorHex = '#202020', this.letterSpacing = 0, this.alignment = 0, this.strikethrough = false});

  final String fontId;
  final String fontVersion;
  final double fontSize;
  final int weight;
  final String colorHex;
  final double letterSpacing;
  final int alignment; // 0 left, 1 center, 2 right
  final bool strikethrough;

  TextStyleSpec copyWith({String? fontId, String? fontVersion, double? fontSize, int? weight, String? colorHex,
          double? letterSpacing, int? alignment, bool? strikethrough}) =>
      TextStyleSpec(
        fontId: fontId ?? this.fontId,
        fontVersion: fontVersion ?? this.fontVersion,
        fontSize: fontSize ?? this.fontSize,
        weight: weight ?? this.weight,
        colorHex: colorHex ?? this.colorHex,
        letterSpacing: letterSpacing ?? this.letterSpacing,
        alignment: alignment ?? this.alignment,
        strikethrough: strikethrough ?? this.strikethrough,
      );

  Map<String, dynamic> toJson() => {
        'fontId': fontId,
        'fontVersion': fontVersion,
        'fontSize': fontSize,
        'weight': weight,
        'colorHex': colorHex,
        'letterSpacing': letterSpacing,
        'baseline': .75,
        'angleDegrees': 0,
        'alignment': alignment,
        'strikethrough': strikethrough,
      };
}

/// Server suggestion for replacing selected words: their box, text and estimated style.
class StyleProposal {
  const StyleProposal({required this.activeRevisionId, required this.originalText, required this.box,
      required this.fontId, required this.fontVersion, required this.fontSizePoints, required this.weight,
      required this.colorHex, required this.letterSpacing, required this.alignment});

  final String? activeRevisionId;
  final String originalText;
  final PageBox box;
  final String? fontId;
  final String? fontVersion;
  final double fontSizePoints;
  final int weight;
  final String colorHex;
  final double letterSpacing;
  final int alignment;

  factory StyleProposal.fromJson(Map<String, dynamic> json) {
    final style = json['style'] as Map<String, dynamic>;
    final candidates = style['candidates'] as List? ?? const [];
    final first = candidates.isEmpty ? null : candidates.first as Map<String, dynamic>;
    return StyleProposal(
      activeRevisionId: json['activeRevisionId'] as String?,
      originalText: json['originalText'] as String? ?? '',
      box: PageBox.fromJson(json['box'] as Map<String, dynamic>),
      fontId: first?['catalogueId'] as String?,
      fontVersion: first?['version'] as String?,
      fontSizePoints: (style['fontSizePoints'] as num?)?.toDouble() ?? 12,
      weight: (style['fontWeight'] as num?)?.toInt() ?? 400,
      colorHex: style['colorHex'] as String? ?? '#202020',
      letterSpacing: (style['letterSpacing'] as num?)?.toDouble() ?? 0,
      alignment: switch (style['alignment']) { 'Center' || 1 => 1, 'Right' || 2 => 2, _ => 0 },
    );
  }
}

class PageHistory {
  const PageHistory({required this.canUndo, required this.canRedo, required this.activeRevisionId});

  final bool canUndo;
  final bool canRedo;
  final String? activeRevisionId;

  factory PageHistory.fromJson(Map<String, dynamic> json) => PageHistory(
        canUndo: json['canUndo'] as bool? ?? false,
        canRedo: json['canRedo'] as bool? ?? false,
        activeRevisionId: json['activeRevisionId'] as String?,
      );
}

/// One eraser stroke: normalized points and brush radius.
class EraseStroke {
  EraseStroke(this.radius) : points = [];

  final double radius;
  final List<List<double>> points;

  Map<String, dynamic> toJson() => {
        'radius': _round(radius),
        'points': [for (final p in points) [_round(p[0]), _round(p[1])]],
      };

  static double _round(double value) => (value * 10000).round() / 10000;
}

/// Revision fields the eraser needs, from the page's `previewRevision` ("crop-N" or a 32-hex Guid).
({String? revisionId, int? cropRevision}) repairSource(String previewRevision) {
  if (previewRevision.startsWith('crop-')) {
    return (revisionId: null, cropRevision: int.tryParse(previewRevision.substring(5)));
  }
  final hex = previewRevision.replaceAll('-', '');
  if (hex.length != 32) return (revisionId: null, cropRevision: null);
  return (
    revisionId: '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-'
        '${hex.substring(16, 20)}-${hex.substring(20)}',
    cropRevision: null,
  );
}

String _newKey() {
  final random = Random.secure();
  final bytes = List<int>.generate(16, (_) => random.nextInt(256));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
}

/// A new random Guid for idempotency keys.
String newIdempotencyKey() => _newKey();

/// Calls for the page editing tools (same endpoints as the web editor).
class EditApi {
  EditApi(this._api, {http.Client? client}) : _http = client ?? http.Client();

  final ApiClient _api;
  final http.Client _http;

  String _page(String documentId, String pageId) => '/documents/$documentId/pages/$pageId';

  // ---- Eraser (page repair) ----

  Future<String> createRepairPreview(String documentId, String pageId,
      {required String previewRevision, List<EraseStroke> strokes = const [], List<List<double>> rectangles = const []}) async {
    final source = repairSource(previewRevision);
    final body = await _api.postJson('${_page(documentId, pageId)}/repair/previews', {
      'sourceRevisionId': source.revisionId,
      'sourceCropRevision': source.cropRevision,
      'rectangles': rectangles,
      'strokes': [for (final stroke in strokes) stroke.toJson()],
    }) as Map<String, dynamic>;
    return body['operationId'] as String;
  }

  Future<String> suggestRepairs(String documentId, String pageId) async =>
      (await _api.postJson('${_page(documentId, pageId)}/repair/suggestions', {}) as Map<String, dynamic>)['operationId']
          as String;

  Future<({String state, List<List<double>> candidates})> getRepair(String documentId, String pageId, String operationId) async {
    final body = await _api.getJson('${_page(documentId, pageId)}/repair/previews/$operationId') as Map<String, dynamic>;
    return (
      state: body['state'] as String,
      candidates: [
        for (final box in (body['candidates'] as List? ?? const []))
          [for (final value in box as List) (value as num).toDouble()],
      ],
    );
  }

  Future<Uint8List> repairPreviewImage(String documentId, String pageId, String operationId) =>
      _api.getBytes('${_page(documentId, pageId)}/repair/previews/$operationId/image');

  Future<void> applyRepair(String documentId, String pageId, String operationId) =>
      _api.postJson('${_page(documentId, pageId)}/repair/previews/$operationId/apply', {});

  // ---- Undo / redo (shared by eraser and text edits) ----

  Future<PageHistory> history(String documentId, String pageId) async =>
      PageHistory.fromJson(await _api.getJson('${_page(documentId, pageId)}/text-edits/history') as Map<String, dynamic>);

  Future<PageHistory> undo(String documentId, String pageId, String? expectedRevisionId) async => PageHistory.fromJson(
      await _api.postJson('${_page(documentId, pageId)}/text-edits/undo', {'expectedRevisionId': expectedRevisionId})
          as Map<String, dynamic>);

  Future<PageHistory> redo(String documentId, String pageId, String? expectedRevisionId) async => PageHistory.fromJson(
      await _api.postJson('${_page(documentId, pageId)}/text-edits/redo', {'expectedRevisionId': expectedRevisionId})
          as Map<String, dynamic>);

  // ---- Tick / cross marks ----

  Future<List<PageMark>> marks(String documentId, String pageId) async => [
        for (final item in await _api.getJson('${_page(documentId, pageId)}/marks') as List)
          PageMark.fromJson(item as Map<String, dynamic>),
      ];

  Future<PageMark> addMark(String documentId, String pageId,
      {required String kind, required PageBox box, required String color, required double strokeWidth}) async {
    final url = _api.url('${_page(documentId, pageId)}/marks');
    final headers = await _api.headers(contentType: 'application/json')
      ..['Idempotency-Key'] = newIdempotencyKey();
    final response = await _http.post(url, headers: headers,
        body: jsonEncode({'kind': kind, 'box': box.toJson(), 'color': color, 'strokeWidth': strokeWidth}));
    return PageMark.fromJson(_json(response) as Map<String, dynamic>);
  }

  Future<PageMark> updateMark(String documentId, String pageId, PageMark mark, {PageBox? box, String? color}) async =>
      PageMark.fromJson(await _api.putJson('${_page(documentId, pageId)}/marks/${mark.id}', {
        'kind': mark.kind,
        'box': (box ?? mark.box).toJson(),
        'color': color ?? mark.color,
        'strokeWidth': mark.strokeWidth,
        'expectedRevision': mark.revision,
      }) as Map<String, dynamic>);

  Future<void> deleteMark(String documentId, String pageId, PageMark mark) =>
      _api.delete('${_page(documentId, pageId)}/marks/${mark.id}?expectedRevision=${mark.revision}');

  // ---- Signatures ----

  Future<List<PageSignature>> signatures(String documentId, String pageId) async => [
        for (final item in await _api.getJson('${_page(documentId, pageId)}/signatures') as List)
          PageSignature.fromJson(item as Map<String, dynamic>),
      ];

  Future<Uint8List> signatureImage(String documentId, String pageId, String signatureId) =>
      _api.getBytes('${_page(documentId, pageId)}/signatures/$signatureId/image');

  /// Removes the paper background from a signature photo; returns a transparent PNG (nothing is stored).
  Future<Uint8List> prepareSignature(Uint8List image, String fileName, {double strength = .5}) async {
    final request = http.MultipartRequest('POST', _api.url('/signatures/prepare'))
      ..headers.addAll(await _api.headers())
      ..fields['strength'] = strength.toString()
      ..fields['keepOriginal'] = 'false'
      ..files.add(http.MultipartFile.fromBytes('image', image, filename: fileName));
    final response = await http.Response.fromStream(await _http.send(request));
    _check(response);
    return response.bodyBytes;
  }

  Future<PageSignature> addSignature(String documentId, String pageId, Uint8List png, PageBox box) async {
    final request = http.MultipartRequest('POST', _api.url('${_page(documentId, pageId)}/signatures'))
      ..headers.addAll(await _api.headers())
      ..headers['Idempotency-Key'] = newIdempotencyKey()
      ..fields['box'] = jsonEncode(box.toJson())
      ..files.add(http.MultipartFile.fromBytes('image', png, filename: 'signature.png'));
    final response = await http.Response.fromStream(await _http.send(request));
    return PageSignature.fromJson(_json(response) as Map<String, dynamic>);
  }

  Future<PageSignature> moveSignature(String documentId, String pageId, PageSignature signature, PageBox box) async =>
      PageSignature.fromJson(await _api.putJson('${_page(documentId, pageId)}/signatures/${signature.id}',
          {'box': box.toJson(), 'expectedRevision': signature.revision}) as Map<String, dynamic>);

  Future<void> deleteSignature(String documentId, String pageId, PageSignature signature) =>
      _api.delete('${_page(documentId, pageId)}/signatures/${signature.id}?expectedRevision=${signature.revision}');

  // ---- OCR ----

  Future<PageOcr> ocr(String documentId, String pageId) async =>
      PageOcr.fromJson(await _api.getJson('${_page(documentId, pageId)}/ocr') as Map<String, dynamic>);

  Future<PageOcr> requestOcr(String documentId, String pageId, {bool retryFailed = false}) async => PageOcr.fromJson(
      await _api.postJson('${_page(documentId, pageId)}/ocr', {'retryFailed': retryFailed}) as Map<String, dynamic>);

  // ---- Text replacement / add text ----

  Future<List<FontFace>> fonts() async => [
        for (final item in await _api.getJson('/text-edit-fonts') as List) FontFace.fromJson(item as Map<String, dynamic>),
      ];

  /// Font file bytes; bundled web assets live on the web host, so only `/api/fonts/...` is fetched from the API.
  Future<Uint8List> fontBytes(String assetUrl) {
    final file = assetUrl.split('/').last;
    return _api.getBytes('/fonts/$file');
  }

  Future<StyleProposal> styleProposal(String documentId, String pageId, String ocrResultId, List<String> wordIds) async =>
      StyleProposal.fromJson(await _api.postJson('${_page(documentId, pageId)}/text-edits/style-proposal',
          {'ocrResultId': ocrResultId, 'wordIds': wordIds}) as Map<String, dynamic>);

  Map<String, dynamic> _editBody({required String? ocrResultId, required String? expectedRevisionId,
          required List<String> wordIds, required String text, required PageBox box, required TextStyleSpec style,
          String? idempotencyKey}) =>
      {
        'ocrResultId': ocrResultId ?? '00000000-0000-0000-0000-000000000000',
        'expectedRevisionId': expectedRevisionId,
        'wordIds': wordIds,
        'replacementText': text,
        'replacementBox': box.toJson(),
        'style': style.toJson(),
        'idempotencyKey': ?idempotencyKey,
      };

  /// Renders the edit on the server and returns the whole page as PNG (nothing is saved).
  Future<Uint8List> previewTextEdit(String documentId, String pageId,
      {required String? ocrResultId, required String? expectedRevisionId, required List<String> wordIds,
      required String text, required PageBox box, required TextStyleSpec style}) async {
    final response = await _http.post(_api.url('${_page(documentId, pageId)}/text-edits/preview'),
        headers: await _api.headers(contentType: 'application/json'),
        body: jsonEncode(_editBody(ocrResultId: ocrResultId, expectedRevisionId: expectedRevisionId, wordIds: wordIds,
            text: text, box: box, style: style)));
    _check(response);
    return response.bodyBytes;
  }

  /// Saves the edit; returns its id. Poll [textEditState] until "Succeeded" or "Failed".
  Future<String> createTextEdit(String documentId, String pageId,
      {required String? ocrResultId, required String? expectedRevisionId, required List<String> wordIds,
      required String text, required PageBox box, required TextStyleSpec style, required String idempotencyKey}) async {
    final body = await _api.postJson('${_page(documentId, pageId)}/text-edits', _editBody(
        ocrResultId: ocrResultId, expectedRevisionId: expectedRevisionId, wordIds: wordIds, text: text, box: box,
        style: style, idempotencyKey: idempotencyKey)) as Map<String, dynamic>;
    return body['editId'] as String;
  }

  Future<({String state, String? failureCode})> textEditState(String documentId, String pageId, String editId) async {
    final body = await _api.getJson('${_page(documentId, pageId)}/text-edits/$editId') as Map<String, dynamic>;
    return (state: body['state'] as String, failureCode: body['failureCode'] as String?);
  }

  dynamic _json(http.Response response) {
    _check(response);
    return response.body.isEmpty ? null : jsonDecode(response.body);
  }

  void _check(http.Response response) {
    if (response.statusCode >= 200 && response.statusCode < 300) return;
    String? code;
    try {
      final body = jsonDecode(response.body);
      if (body is Map<String, dynamic>) code = body['code'] as String?;
    } catch (_) {
      // Not JSON.
    }
    throw ApiException(response.statusCode, code);
  }
}
