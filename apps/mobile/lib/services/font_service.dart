import 'package:flutter/services.dart';
import 'package:http/http.dart' as http;

import '../api/edit_api.dart';

/// Loads catalogue fonts on demand for the text editor's live preview (like the web's font service).
/// Bundled faces come from the website's public assets; large world-script faces from the API.
/// The server render ("Preview") is always the exact result, so a font that fails to load only
/// affects the live guide.
class FontService {
  FontService(this._edits, this._webBaseUrl, {http.Client? client}) : _http = client ?? http.Client();

  final EditApi _edits;
  final String _webBaseUrl;
  final http.Client _http;
  final _loaded = <String, Future<bool>>{};
  List<FontFace>? _faces;

  Future<List<FontFace>> faces() async => _faces ??= await _edits.fonts();

  /// Family name to use in TextStyle once [face] is loaded; returns false when it could not be loaded.
  Future<bool> load(FontFace face) => _loaded.putIfAbsent('${face.catalogueId}:${face.version}', () async {
        try {
          final bytes = face.assetUrl.startsWith('/api/')
              ? await _edits.fontBytes(face.assetUrl)
              : await _download(face.assetUrl);
          final loader = FontLoader(face.webFamilyName)..addFont(Future.value(ByteData.sublistView(bytes)));
          await loader.load();
          return true;
        } catch (_) {
          _loaded.remove('${face.catalogueId}:${face.version}');
          return false;
        }
      });

  Future<Uint8List> _download(String path) async {
    final response = await _http.get(Uri.parse('${_webBaseUrl.replaceAll(RegExp(r'/+$'), '')}$path'));
    if (response.statusCode != 200) throw StateError('font ${response.statusCode}');
    return response.bodyBytes;
  }
}
