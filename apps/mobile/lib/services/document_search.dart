import '../api/edit_api.dart';

/// One match: the page and the words that make it up.
class SearchHit {
  const SearchHit({required this.pageId, required this.pageIndex, required this.wordIds, required this.snippet});

  final String pageId;
  final int pageIndex;
  final List<String> wordIds;
  final String snippet;
}

/// Client-side search over recognised words, matching the web (document-search.ts):
/// case- and accent-insensitive, whitespace collapsed, at least 2 characters.
abstract final class DocumentSearch {
  static const minimumLength = 2;

  static String normalize(String text) => _stripMarks(text.toLowerCase()).replaceAll(RegExp(r'\s+'), ' ').trim();

  static List<SearchHit> find(String query, List<(String pageId, PageOcr ocr)> pages) {
    final needle = normalize(query);
    if (needle.length < minimumLength) return const [];
    final hits = <SearchHit>[];
    for (var index = 0; index < pages.length; index++) {
      final (pageId, ocr) = pages[index];
      if (!ocr.isReady || ocr.words.isEmpty) continue;
      // Join words with single spaces and remember where each word starts in the joined text.
      final starts = <int>[];
      final buffer = StringBuffer();
      for (final word in ocr.words) {
        if (buffer.isNotEmpty) buffer.write(' ');
        starts.add(buffer.length);
        buffer.write(normalize(word.text));
      }
      final haystack = buffer.toString();
      var from = 0;
      for (;;) {
        final at = haystack.indexOf(needle, from);
        if (at < 0) break;
        final end = at + needle.length;
        final ids = <String>[];
        for (var w = 0; w < ocr.words.length; w++) {
          final wordEnd = starts[w] + normalize(ocr.words[w].text).length;
          if (starts[w] < end && wordEnd > at) ids.add(ocr.words[w].id);
        }
        final first = ocr.words.indexWhere((word) => word.id == ids.first);
        final snippet = ocr.words.skip((first - 4).clamp(0, ocr.words.length)).take(10).map((w) => w.text).join(' ');
        hits.add(SearchHit(pageId: pageId, pageIndex: index, wordIds: ids, snippet: snippet));
        from = end;
      }
    }
    return hits;
  }

  /// Removes accents (é → e) for the common Latin ranges, like the web's NFD + strip marks.
  static String _stripMarks(String text) {
    const from = 'àáâãäåāăąçćčďèéêëēėęěìíîïīįłñńňòóôõöøōőřśšşťùúûüūůűųýÿžźż';
    const to = 'aaaaaaaaacccdeeeeeeeeiiiiiilnnnoooooooorsssstuuuuuuuuyyzzz';
    final out = StringBuffer();
    for (final rune in text.runes) {
      final char = String.fromCharCode(rune);
      final index = from.indexOf(char);
      if (rune >= 0x0300 && rune <= 0x036f) continue; // combining marks
      out.write(index >= 0 ? to[index] : char);
    }
    return out.toString();
  }
}
