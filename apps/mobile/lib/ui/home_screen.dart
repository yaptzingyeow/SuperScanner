import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../services/auth_service.dart';
import '../services/local_store.dart';
import '../services/session_state.dart';
import '../services/upload_service.dart';
import 'common.dart';

/// My documents: Scan and Import first, then documents on this phone and (in progress or Pro) in the cloud.
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  late Future<List<DocumentSummary>> _cloud;
  bool _uploading = false;

  @override
  void initState() {
    super.initState();
    _cloud = _loadCloud();
  }

  Future<List<DocumentSummary>> _loadCloud() => context.read<DocumentsApi>().listDocuments();

  Future<void> _refresh() async {
    setState(() => _cloud = _loadCloud());
    await Future.wait([_cloud, context.read<SessionState>().refresh()]);
  }

  Future<void> _capture(Future<List<UploadFile>> Function() source) async {
    final List<UploadFile> files;
    try {
      files = await source();
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
      return;
    }
    if (files.isEmpty || !mounted) return;
    setState(() => _uploading = true);
    try {
      final id = await context.read<UploadService>().uploadNewDocument(UploadService.defaultTitle(DateTime.now()), files);
      if (!mounted) return;
      await context.push('/documents/$id');
      if (mounted) await _refresh();
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  Future<void> _signOut() async {
    final local = context.read<LocalStore>();
    if (local.documents.isNotEmpty) {
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: Text(context.l.signOutQuestion),
          content: Text(context.l.signOutWarning(local.documents.length)),
          actions: [
            TextButton(onPressed: () => Navigator.pop(context, false), child: Text(context.l.cancel)),
            FilledButton(onPressed: () => Navigator.pop(context, true), child: Text(context.l.signOut)),
          ],
        ),
      );
      if (confirmed != true) return;
    }
    if (!mounted) return;
    await context.read<LocalStore>().clear();
    if (!mounted) return;
    context.read<SessionState>().clear();
    await context.read<AuthService>().signOut();
  }

  @override
  Widget build(BuildContext context) {
    final local = context.watch<LocalStore>().documents;
    final session = context.watch<SessionState>();
    final dates = DateFormat.yMMMd(Localizations.localeOf(context).toLanguageTag()).add_Hm();
    return Scaffold(
      appBar: AppBar(
        title: Text(context.l.myDocuments),
        actions: [
          PopupMenuButton<String>(
            onSelected: (value) => switch (value) {
              'settings' => context.push('/settings'),
              'signout' => _signOut(),
              _ => null,
            },
            itemBuilder: (context) => [
              PopupMenuItem(value: 'settings', child: Text(context.l.settingsPrivacy)),
              PopupMenuItem(value: 'signout', child: Text(context.l.signOut)),
            ],
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: _refresh,
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 120),
          children: [
            _ActionCard(busy: _uploading, onScan: () => _capture(scanPages), onImport: () => _capture(pickFiles)),
            if (local.isNotEmpty) ...[
              _SectionTitle(context.l.onThisPhone),
              for (final document in local)
                Card(
                  child: ListTile(
                    leading: const Icon(Icons.phone_android),
                    title: Text(document.title),
                    subtitle: Text(context.l.pagesAndDate(document.pageCount, dates.format(document.savedAt))),
                    onTap: () => context.push('/local/${document.id}'),
                  ),
                ),
            ],
            FutureBuilder<List<DocumentSummary>>(
              future: _cloud,
              builder: (context, snapshot) {
                if (snapshot.hasError) {
                  return Padding(padding: const EdgeInsets.all(16), child: Text(friendlyError(context, snapshot.error!)));
                }
                final cloud = (snapshot.data ?? const <DocumentSummary>[])
                    .where((document) => !local.any((saved) => saved.id == document.id))
                    .toList();
                if (snapshot.connectionState != ConnectionState.done) {
                  return const Padding(padding: EdgeInsets.all(24), child: Center(child: CircularProgressIndicator()));
                }
                if (cloud.isEmpty && local.isEmpty) {
                  return Padding(
                    padding: const EdgeInsets.all(32),
                    child: Text(context.l.noDocuments, textAlign: TextAlign.center),
                  );
                }
                return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  if (cloud.isNotEmpty) _SectionTitle(session.isPro ? context.l.inYourAccount : context.l.inProgress),
                  for (final document in cloud)
                    Card(
                      child: ListTile(
                        leading: Icon(session.isPro ? Icons.cloud_outlined : Icons.edit_document),
                        title: Text(document.title),
                        subtitle: Text(context.l.pagesAndDate(document.pageCount, dates.format(document.updatedAt.toLocal()))),
                        onTap: () async {
                          await context.push('/documents/${document.id}');
                          if (mounted) await _refresh();
                        },
                      ),
                    ),
                ]);
              },
            ),
          ],
        ),
      ),
    );
  }
}

class _ActionCard extends StatelessWidget {
  const _ActionCard({required this.busy, required this.onScan, required this.onImport});

  final bool busy;
  final VoidCallback onScan;
  final VoidCallback onImport;

  @override
  Widget build(BuildContext context) => Card(
        color: Brand.greenSoft,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: busy
              ? Column(children: [
                  const CircularProgressIndicator(),
                  const SizedBox(height: 12),
                  Text(context.l.uploadingPages),
                ])
              : Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  FilledButton.icon(
                    style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(64)),
                    onPressed: onScan,
                    icon: const Icon(Icons.document_scanner_outlined, size: 28),
                    label: Text(context.l.scan, style: const TextStyle(fontSize: 20)),
                  ),
                  const SizedBox(height: 8),
                  OutlinedButton.icon(
                    onPressed: onImport,
                    icon: const Icon(Icons.photo_library_outlined),
                    label: Text(context.l.importPhotosPdf),
                  ),
                ]),
        ),
      );
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(4, 20, 4, 8),
        child: Text(text, style: Theme.of(context).textTheme.titleMedium),
      );
}
