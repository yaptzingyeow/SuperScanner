import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:printing/printing.dart';
import 'package:provider/provider.dart';
import 'package:share_plus/share_plus.dart';

import '../services/local_store.dart';
import '../services/upload_service.dart';
import 'common.dart';

/// A document saved on this phone: view pages, share or print the PDF, or edit again.
class LocalDocumentScreen extends StatefulWidget {
  const LocalDocumentScreen({super.key, required this.documentId});

  final String documentId;

  @override
  State<LocalDocumentScreen> createState() => _LocalDocumentScreenState();
}

class _LocalDocumentScreenState extends State<LocalDocumentScreen> {
  bool _busy = false;

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final store = context.watch<LocalStore>();
    final document = store.documents.where((item) => item.id == widget.documentId).firstOrNull;
    if (document == null) {
      return Scaffold(appBar: AppBar(), body: Center(child: Text(context.l.noLongerOnPhone)));
    }
    final pdf = store.pdfFile(document.id);
    return Scaffold(
      appBar: AppBar(
        title: Text(document.title),
        actions: [
          PopupMenuButton<String>(
            onSelected: (value) async {
              if (value == 'rename') {
                final controller = TextEditingController(text: document.title);
                final title = await showDialog<String>(
                  context: context,
                  builder: (context) => AlertDialog(
                    title: Text(context.l.rename),
                    content: TextField(controller: controller, autofocus: true, maxLength: 200),
                    actions: [
                      TextButton(onPressed: () => Navigator.pop(context), child: Text(context.l.cancel)),
                      FilledButton(onPressed: () => Navigator.pop(context, controller.text.trim()),
                          child: Text(context.l.save)),
                    ],
                  ),
                );
                if (title != null && title.isNotEmpty) await store.rename(document.id, title);
              } else if (value == 'delete') {
                final confirmed = await showDialog<bool>(
                  context: context,
                  builder: (context) => AlertDialog(
                    title: Text(context.l.deleteFromPhone),
                    content: Text(context.l.cannotUndo),
                    actions: [
                      TextButton(onPressed: () => Navigator.pop(context, false), child: Text(context.l.cancel)),
                      FilledButton(onPressed: () => Navigator.pop(context, true), child: Text(context.l.delete)),
                    ],
                  ),
                );
                if (confirmed == true) {
                  await store.remove(document.id);
                  if (context.mounted) context.pop();
                }
              }
            },
            itemBuilder: (context) => [
              PopupMenuItem(value: 'rename', child: Text(context.l.rename)),
              PopupMenuItem(value: 'delete', child: Text(context.l.delete)),
            ],
          ),
        ],
      ),
      body: Column(children: [
        if (_busy) const LinearProgressIndicator(),
        Expanded(
          child: PageView.builder(
            itemCount: document.pageCount,
            itemBuilder: (context, index) => InteractiveViewer(
              maxScale: 6,
              child: Center(child: Image.file(store.pageFile(document.id, index + 1))),
            ),
          ),
        ),
        Padding(padding: const EdgeInsets.all(8), child: Text(context.l.swipeForMore(document.pageCount))),
      ]),
      bottomNavigationBar: SafeArea(
        child: Row(mainAxisAlignment: MainAxisAlignment.spaceEvenly, children: [
          TextButton.icon(
            onPressed: _busy
                ? null
                : () => _run(() => SharePlus.instance.share(ShareParams(
                    files: [XFile(pdf.path, mimeType: 'application/pdf')], subject: document.title))),
            icon: const Icon(Icons.ios_share),
            label: Text(context.l.share),
          ),
          TextButton.icon(
            onPressed: _busy
                ? null
                : () => _run(() async {
                      final bytes = await pdf.readAsBytes();
                      await Printing.layoutPdf(name: document.title, onLayout: (_) async => bytes);
                    }),
            icon: const Icon(Icons.print_outlined),
            label: Text(context.l.print),
          ),
          TextButton.icon(
            onPressed: _busy
                ? null
                : () => _run(() async {
                      final uploads = context.read<UploadService>();
                      final files = [
                        for (var number = 1; number <= document.pageCount; number++)
                          UploadFile(name: 'page-$number.jpg', mediaType: 'image/jpeg',
                              bytes: await store.pageFile(document.id, number).readAsBytes()),
                      ];
                      final id = await uploads.uploadNewDocument(document.title, files);
                      if (context.mounted) await context.push('/documents/$id');
                    }),
            icon: const Icon(Icons.edit_outlined),
            label: Text(context.l.editAgain),
          ),
        ]),
      ),
    );
  }
}
