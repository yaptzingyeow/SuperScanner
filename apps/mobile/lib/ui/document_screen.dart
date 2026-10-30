import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:path_provider/path_provider.dart';
import 'package:printing/printing.dart';
import 'package:provider/provider.dart';
import 'package:share_plus/share_plus.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../services/export_service.dart';
import '../services/session_state.dart';
import '../services/upload_service.dart';
import 'common.dart';
import 'export_options_sheet.dart';

/// A document on the server while it is being edited: pages appear as they finish processing.
class DocumentScreen extends StatefulWidget {
  const DocumentScreen({super.key, required this.documentId});

  final String documentId;

  @override
  State<DocumentScreen> createState() => _DocumentScreenState();
}

class _DocumentScreenState extends State<DocumentScreen> {
  DocumentDetail? _document;
  Object? _error;
  Timer? _poll;
  String? _busy;

  DocumentsApi get _api => context.read<DocumentsApi>();

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final document = await _api.getDocument(widget.documentId);
      if (!mounted) return;
      setState(() {
        _document = document;
        _error = null;
      });
      _poll?.cancel();
      if (document.isBusy) _poll = Timer(const Duration(seconds: 2), _load);
    } catch (error) {
      if (mounted) setState(() => _error = error);
    }
  }

  Future<void> _run(String label, Future<void> Function() action) async {
    setState(() => _busy = label);
    try {
      await action();
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    } finally {
      if (mounted) setState(() => _busy = null);
    }
  }

  Future<void> _rename() async {
    final controller = TextEditingController(text: _document?.title);
    final title = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(context.l.rename),
        content: TextField(controller: controller, autofocus: true, maxLength: 200),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: Text(context.l.cancel)),
          FilledButton(onPressed: () => Navigator.pop(context, controller.text.trim()), child: Text(context.l.save)),
        ],
      ),
    );
    if (title == null || title.isEmpty || !mounted) return;
    await _run(context.l.processing, () async {
      await _api.renameDocument(widget.documentId, title);
      await _load();
    });
  }

  /// [to] is already adjusted for the removed item (onReorderItem semantics).
  Future<void> _reorder(int from, int to) async {
    final document = _document!;
    final pages = [...document.pages];
    final moved = pages.removeAt(from);
    pages.insert(to, moved);
    setState(() => _document = DocumentDetail(id: document.id, title: document.title, status: document.status,
        revision: document.revision, pageOrderRevision: document.pageOrderRevision, pages: pages,
        imports: document.imports));
    await _run(context.l.processing, () async {
      await _api.reorderPages(document.id, document.pageOrderRevision, [for (final page in pages) page.id]);
      await _load();
    });
  }

  Future<void> _removePage(DocumentPage page) => _run(context.l.processing, () async {
        await _api.removePage(widget.documentId, page.id);
        await _load();
      });

  Future<void> _addPages(Future<List<UploadFile>> Function() source) async {
    final files = await source();
    if (files.isEmpty || !mounted) return;
    final uploads = context.read<UploadService>();
    await _run(context.l.uploadingPages, () async {
      for (final file in files) {
        await uploads.upload(widget.documentId, file);
      }
      await _load();
    });
  }

  Future<Uint8List> _pdf(WatermarkSettings? watermark) =>
      context.read<ExportService>().buildPdf(widget.documentId, watermark: watermark);

  /// Export options (watermark) before an export; null when the user cancelled.
  Future<({WatermarkSettings? watermark})?> _options(String action) {
    final document = _document!;
    final first = document.pages.where((page) => page.hasPreview).firstOrNull;
    return showExportOptions(context, action: action, firstPage: () async {
      if (first == null) throw StateError('no page');
      return _api.pageImage(document.id, first.id, 'preview');
    });
  }

  Future<void> _share() async {
    final options = await _options(context.l.share);
    if (options == null || !mounted) return;
    await _run(context.l.processing, () async {
      final bytes = await _pdf(options.watermark);
      final file = File('${(await getTemporaryDirectory()).path}/${_safeName(_document!.title)}.pdf');
      await file.writeAsBytes(bytes, flush: true);
      await SharePlus.instance.share(ShareParams(files: [XFile(file.path, mimeType: 'application/pdf')],
          subject: _document!.title));
    });
  }

  Future<void> _print() async {
    final options = await _options(context.l.print);
    if (options == null || !mounted) return;
    await _run(context.l.processing, () async {
      final bytes = await _pdf(options.watermark);
      await Printing.layoutPdf(name: _document!.title, onLayout: (_) async => bytes);
    });
  }

  Future<void> _saveToPhone() async {
    final pro = context.read<SessionState>().isPro;
    final exports = context.read<ExportService>();
    final l = context.l;
    final options = await _options(pro ? l.saveCopy : l.saveToPhone);
    if (options == null || !mounted) return;
    await _run(l.saveToPhone, () async {
      await exports.saveToPhone(_document!, removeFromServer: !pro, watermark: options.watermark);
      if (!mounted) return;
      showMessage(context, pro ? l.savedToPhone : l.savedToPhoneFree);
      context.pop();
    });
  }

  static String _safeName(String title) => title.replaceAll(RegExp(r'[^\w\- ]'), '_').trim();

  @override
  Widget build(BuildContext context) {
    final document = _document;
    final pro = context.watch<SessionState>().isPro;
    return Scaffold(
      appBar: AppBar(
        title: GestureDetector(onTap: document == null ? null : _rename, child: Text(document?.title ?? context.l.document)),
        actions: [
          IconButton(
            tooltip: context.l.search,
            onPressed: document == null ? null : () => context.push('/documents/${widget.documentId}/search'),
            icon: const Icon(Icons.search),
          ),
          IconButton(tooltip: context.l.rename, onPressed: document == null ? null : _rename, icon: const Icon(Icons.edit)),
        ],
      ),
      body: _error != null
          ? Center(child: Text(friendlyError(context, _error!)))
          : document == null
              ? const Center(child: CircularProgressIndicator())
              : Column(children: [
                  if (_busy != null) LinearProgressIndicator(semanticsLabel: _busy),
                  if (document.isBusy)
                    Padding(padding: const EdgeInsets.all(8), child: Text(context.l.processingPages)),
                  Expanded(
                    child: ReorderableListView.builder(
                      padding: const EdgeInsets.all(12),
                      itemCount: document.pages.length,
                      onReorderItem: _reorder,
                      itemBuilder: (context, index) {
                        final page = document.pages[index];
                        return Card(
                          key: ValueKey(page.id),
                          child: ListTile(
                            contentPadding: const EdgeInsets.all(8),
                            leading: SizedBox(
                              width: 56,
                              height: 72,
                              child: page.hasPreview
                                  ? ApiImage(documentId: document.id, pageId: page.id, asset: 'thumbnail',
                                      revision: page.previewRevision, fit: BoxFit.cover)
                                  : page.isFailed
                                      ? const Icon(Icons.error_outline, color: Brand.danger)
                                      : const Center(child: CircularProgressIndicator(strokeWidth: 2)),
                            ),
                            title: Text(context.l.pageNumber(index + 1)),
                            subtitle: Text(page.isFailed
                                ? context.l.pageFailed
                                : page.isSettled
                                    ? lookLabel(context, page.appliedFilter)
                                    : context.l.processing),
                            trailing: IconButton(
                              tooltip: context.l.removePage,
                              icon: const Icon(Icons.delete_outline),
                              onPressed: _busy == null ? () => _removePage(page) : null,
                            ),
                            onTap: page.isSettled
                                ? () async {
                                    await context.push('/documents/${document.id}/pages/${page.id}');
                                    if (mounted) await _load();
                                  }
                                : null,
                          ),
                        );
                      },
                    ),
                  ),
                ]),
      bottomNavigationBar: document == null
          ? null
          : SafeArea(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(8, 0, 8, 8),
                child: Wrap(alignment: WrapAlignment.spaceEvenly, children: [
                  _BarButton(icon: Icons.add_a_photo_outlined, label: context.l.add,
                      onPressed: _busy == null ? () => _addPages(scanPages) : null),
                  _BarButton(icon: Icons.ios_share, label: context.l.share,
                      onPressed: _busy == null && !document.isBusy ? _share : null),
                  _BarButton(icon: Icons.print_outlined, label: context.l.print,
                      onPressed: _busy == null && !document.isBusy ? _print : null),
                  _BarButton(icon: Icons.save_alt, label: pro ? context.l.saveCopy : context.l.saveToPhone,
                      onPressed: _busy == null && !document.isBusy ? _saveToPhone : null),
                ]),
              ),
            ),
    );
  }
}

class _BarButton extends StatelessWidget {
  const _BarButton({required this.icon, required this.label, required this.onPressed});

  final IconData icon;
  final String label;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) => TextButton(
        onPressed: onPressed,
        child: Column(mainAxisSize: MainAxisSize.min, children: [Icon(icon), const SizedBox(height: 4), Text(label)]),
      );
}
