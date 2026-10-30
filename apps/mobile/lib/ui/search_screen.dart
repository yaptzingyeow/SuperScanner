import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../api/api_client.dart';
import '../api/edit_api.dart';
import '../api/models.dart';
import '../services/document_search.dart';
import 'common.dart';

/// Search recognised text across the document; pages not yet recognised can be recognised here.
class SearchScreen extends StatefulWidget {
  const SearchScreen({super.key, required this.documentId});

  final String documentId;

  @override
  State<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends State<SearchScreen> {
  final _query = TextEditingController();
  DocumentDetail? _document;
  final _ocr = <String, PageOcr>{};
  bool _busy = false;
  Timer? _poll;

  EditApi get _edits => context.read<EditApi>();

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _poll?.cancel();
    _query.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final document = await context.read<DocumentsApi>().getDocument(widget.documentId);
      final pages = document.pages.where((page) => page.isSettled).toList();
      final results = await Future.wait(pages.map((page) => _edits.ocr(widget.documentId, page.id)));
      if (!mounted) return;
      setState(() {
        _document = document;
        for (var i = 0; i < pages.length; i++) {
          _ocr[pages[i].id] = results[i];
        }
      });
      _poll?.cancel();
      if (_ocr.values.any((ocr) => ocr.isWorking)) _poll = Timer(const Duration(seconds: 2), _load);
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    }
  }

  Future<void> _recogniseAll() async {
    setState(() => _busy = true);
    try {
      for (final entry in _ocr.entries.where((entry) => entry.value.canRequest)) {
        await _edits.requestOcr(widget.documentId, entry.key, retryFailed: entry.value.state == 'Failed');
      }
      await _load();
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l = context.l;
    final document = _document;
    final pages = document?.pages.where((page) => _ocr.containsKey(page.id)).toList() ?? const <DocumentPage>[];
    final hits = DocumentSearch.find(_query.text, [for (final page in pages) (page.id, _ocr[page.id]!)]);
    final unrecognised = _ocr.values.where((ocr) => !ocr.isReady).length;
    final working = _ocr.values.any((ocr) => ocr.isWorking);
    return Scaffold(
      appBar: AppBar(
        title: TextField(
          controller: _query,
          autofocus: true,
          decoration: InputDecoration(hintText: l.searchPlaceholder, border: InputBorder.none),
          onChanged: (_) => setState(() {}),
        ),
      ),
      body: document == null
          ? const Center(child: CircularProgressIndicator())
          : ListView(children: [
              if (_busy || working) const LinearProgressIndicator(),
              if (unrecognised > 0)
                ListTile(
                  leading: const Icon(Icons.info_outline),
                  title: Text(l.pagesNotRecognised(unrecognised)),
                  trailing: TextButton(
                    onPressed: _busy || working || !_ocr.values.any((ocr) => ocr.canRequest) ? null : _recogniseAll,
                    child: Text(l.recogniseText),
                  ),
                ),
              if (_query.text.trim().length >= DocumentSearch.minimumLength && hits.isEmpty)
                Padding(padding: const EdgeInsets.all(24), child: Text(l.noMatches, textAlign: TextAlign.center)),
              if (hits.isNotEmpty)
                Padding(padding: const EdgeInsets.all(12), child: Text(l.matchCount(hits.length))),
              for (final hit in hits)
                ListTile(
                  leading: CircleAvatar(child: Text('${hit.pageIndex + 1}')),
                  title: Text(hit.snippet, maxLines: 2, overflow: TextOverflow.ellipsis),
                  subtitle: Text(l.pageNumber(hit.pageIndex + 1)),
                  onTap: () => context.push('/documents/${widget.documentId}/pages/${hit.pageId}'),
                ),
            ]),
    );
  }
}
