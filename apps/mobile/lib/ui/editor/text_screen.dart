import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../api/api_client.dart';
import '../../api/edit_api.dart';
import '../../l10n/app_localizations.dart';
import '../../services/font_service.dart';
import '../common.dart';
import 'marks_screen.dart' show parseHex;
import 'page_stage.dart';

/// Recognise text, then replace words or add new text. The server renders every edit, so the
/// result matches the website; the live strip above the keyboard is only a guide.
class TextScreen extends StatefulWidget {
  const TextScreen({super.key, required this.documentId, required this.pageId});

  final String documentId;
  final String pageId;

  @override
  State<TextScreen> createState() => _TextScreenState();
}

enum _Mode { replace, add }

/// The selection after tapping [tapped]. The server replaces only a run of neighbouring words on
/// one line, so a tap on the same line extends the run to that word, a tap on a selected end word
/// drops it, and anything else starts a new selection ([restarted] is true when a run was dropped).
({Set<String> selection, bool restarted}) textSelectionAfterTap(List<OcrWord> words, Set<String> selected, OcrWord tapped) {
  final line = words.where((word) => word.lineId == tapped.lineId).toList()
    ..sort((a, b) => a.readingOrder.compareTo(b.readingOrder));
  final picked = [for (var i = 0; i < line.length; i++) if (selected.contains(line[i].id)) i];
  final index = line.indexWhere((word) => word.id == tapped.id);
  if (selected.isEmpty) return (selection: {tapped.id}, restarted: false);
  if (picked.length != selected.length) return (selection: {tapped.id}, restarted: true);
  if (selected.contains(tapped.id)) {
    if (index == picked.first || index == picked.last) return (selection: {...selected}..remove(tapped.id), restarted: false);
    return (selection: {tapped.id}, restarted: false);
  }
  final from = index < picked.first ? index : picked.first;
  final to = index > picked.last ? index : picked.last;
  return (selection: {for (var i = from; i <= to; i++) line[i].id}, restarted: false);
}

class _TextScreenState extends State<TextScreen> {
  /// Kept for the session so the panel stays as see-through as the user left it.
  static double _panelOpacity = .95;

  PageImage? _page;
  PageOcr? _ocr;
  PageHistory? _history;
  final _selected = <String>{};
  _Mode _mode = _Mode.replace;
  bool _busy = false;
  bool _changed = false;
  Object? _error;
  Timer? _poll;

  /// The edit being made in the panel; its box is drawn (and, for new text, dragged) on the page.
  _Draft? _draft;
  bool _panelHidden = false;

  EditApi get _edits => context.read<EditApi>();

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
      final results = await Future.wait([
        PageImage.load(context.read<DocumentsApi>(), widget.documentId, widget.pageId),
        _edits.ocr(widget.documentId, widget.pageId),
        _edits.history(widget.documentId, widget.pageId),
      ]);
      if (!mounted) return;
      final ocr = results[1] as PageOcr;
      setState(() {
        _page = results[0] as PageImage;
        _ocr = ocr;
        _history = results[2] as PageHistory;
        _selected.clear();
        _error = null;
      });
      _schedulePoll(ocr);
    } catch (error) {
      if (mounted) setState(() => _error = error);
    }
  }

  void _schedulePoll(PageOcr ocr) {
    _poll?.cancel();
    if (!ocr.isWorking) return;
    _poll = Timer(const Duration(seconds: 3), () async {
      try {
        final next = await _edits.ocr(widget.documentId, widget.pageId);
        if (!mounted) return;
        setState(() => _ocr = next);
        _schedulePoll(next);
      } catch (_) {
        if (mounted) _schedulePoll(ocr);
      }
    });
  }

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

  Future<void> _recognise() => _run(() async {
        final ocr = await _edits.requestOcr(widget.documentId, widget.pageId, retryFailed: _ocr?.state == 'Failed');
        if (!mounted) return;
        setState(() => _ocr = ocr);
        _schedulePoll(ocr);
      });

  Future<void> _undoRedo(bool undo) => _run(() async {
        final active = _history?.activeRevisionId;
        if (undo) {
          await _edits.undo(widget.documentId, widget.pageId, active);
        } else {
          await _edits.redo(widget.documentId, widget.pageId, active);
        }
        _changed = true;
        await _load();
      });

  void _tap(Offset point) {
    final ocr = _ocr;
    final page = _page;
    final draft = _draft;
    if (page == null || _busy) return;
    if (draft != null) {
      // New text follows the tap; a replacement stays on the words it replaces.
      if (draft.isAdd) {
        setState(() => draft.box = PageBox(point.dx - draft.box.width / 2, point.dy - draft.box.height / 2,
            draft.box.width, draft.box.height).clamp());
      }
      return;
    }
    if (_mode == _Mode.add) {
      _openEditor(_Draft.add(point, page.size));
      return;
    }
    if (ocr == null || !ocr.isReady) return;
    final word = ocr.words.where((word) => _hit(word.box, point)).firstOrNull;
    if (word == null) return;
    final next = textSelectionAfterTap(ocr.words, _selected, word);
    setState(() => _selected
      ..clear()
      ..addAll(next.selection));
    if (next.restarted) showMessage(context, context.l.selectOneLine);
  }

  static bool _hit(PageBox box, Offset point) =>
      point.dx >= box.x - .004 && point.dx <= box.right + .004 && point.dy >= box.y - .004 && point.dy <= box.bottom + .004;

  Future<void> _editSelection() => _run(() async {
        final ocr = _ocr!;
        final words = ocr.words.where((word) => _selected.contains(word.id)).map((word) => word.id).toList();
        final proposal = await _edits.styleProposal(widget.documentId, widget.pageId, ocr.resultId!, words);
        if (!mounted) return;
        final fonts = await context.read<FontService>().faces();
        final face = fonts.where((f) => f.catalogueId == proposal.fontId && f.version == proposal.fontVersion).firstOrNull ??
            fonts.where((f) => f.weight == 400).first;
        await _openEditor(_Draft(
          ocrResultId: ocr.resultId,
          wordIds: words,
          expectedRevisionId: proposal.activeRevisionId,
          originalText: proposal.originalText,
          text: proposal.originalText,
          box: proposal.box,
          style: TextStyleSpec(
            fontId: face.catalogueId,
            fontVersion: face.version,
            fontSize: (proposal.fontSizePoints / _page!.size.height).clamp(.002, 1.0),
            weight: proposal.weight,
            colorHex: proposal.colorHex.length >= 7 ? proposal.colorHex.substring(0, 7) : '#202020',
            letterSpacing: proposal.letterSpacing.clamp(-.1, 3.0),
            alignment: proposal.alignment,
          ),
        ));
      });

  Future<void> _openEditor(_Draft draft) async {
    if (draft.ocrResultId == null) draft.expectedRevisionId = _history?.activeRevisionId;
    setState(() {
      _draft = draft;
      _panelHidden = false;
    });
  }

  Future<void> _closeEditor(bool saved) async {
    setState(() => _draft = null);
    if (saved) {
      _changed = true;
      await _load();
    }
  }

  /// The draft's text drawn where it will go, sized like the server render (font size is a share of page height).
  Widget _draftText(_Draft draft, Rect pageRect) {
    final style = draft.style;
    final color = parseHex(style.colorHex);
    return IgnorePointer(
      child: Align(
        alignment: [Alignment.centerLeft, Alignment.center, Alignment.centerRight][style.alignment],
        child: Text(
          draft.text.isEmpty ? ' ' : draft.text,
          maxLines: 1,
          softWrap: false,
          overflow: TextOverflow.visible,
          style: TextStyle(
            fontFamily: draft.family,
            fontSize: style.fontSize * pageRect.height,
            fontWeight: FontWeight.values[((style.weight ~/ 100) - 1).clamp(0, 8)],
            letterSpacing: style.letterSpacing * style.fontSize * pageRect.height,
            color: color,
            decoration: style.strikethrough ? TextDecoration.lineThrough : null,
            decorationColor: color,
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final page = _page;
    final ocr = _ocr;
    final l = context.l;
    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) Navigator.pop(context, _changed);
      },
      child: Scaffold(
        appBar: AppBar(title: Text(l.toolText), actions: [
          IconButton(tooltip: l.undo, onPressed: _busy || !(_history?.canUndo ?? false) ? null : () => _undoRedo(true),
              icon: const Icon(Icons.undo)),
          IconButton(tooltip: l.redo, onPressed: _busy || !(_history?.canRedo ?? false) ? null : () => _undoRedo(false),
              icon: const Icon(Icons.redo)),
        ]),
        body: _error != null
            ? Center(child: Text(friendlyError(context, _error!)))
            : page == null || ocr == null
                ? const Center(child: CircularProgressIndicator())
                : Column(children: [
                    if (_busy || ocr.isWorking) const LinearProgressIndicator(),
                    Padding(
                      padding: const EdgeInsets.all(8),
                      child: SegmentedButton<_Mode>(
                        segments: [
                          ButtonSegment(value: _Mode.replace, icon: const Icon(Icons.find_replace), label: Text(l.replaceText)),
                          ButtonSegment(value: _Mode.add, icon: const Icon(Icons.text_fields), label: Text(l.addText)),
                        ],
                        selected: {_mode},
                        onSelectionChanged: (value) => setState(() {
                          _mode = value.first;
                          _selected.clear();
                          _draft = null;
                        }),
                      ),
                    ),
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 8),
                      child: Text(_draft?.isAdd ?? false ? l.moveTextHint : _hint(context, ocr), textAlign: TextAlign.center),
                    ),
                    Expanded(
                      child: Stack(children: [
                        Positioned.fill(
                          child: InteractiveViewer(
                            maxScale: 6,
                            child: PageStage(
                              image: MemoryImage(page.bytes),
                              imageSize: page.size,
                              onTapPage: _tap,
                              builder: (context, rect) => [
                                if (_mode == _Mode.replace && ocr.isReady && _draft == null)
                                  for (final word in ocr.words)
                                    Positioned.fromRect(
                                      rect: PageStage.toRect(rect, word.box).inflate(1),
                                      child: IgnorePointer(
                                        child: DecoratedBox(
                                          decoration: BoxDecoration(
                                            color: _selected.contains(word.id) ? const Color(0x553F8CFF) : const Color(0x1A3F8CFF),
                                            border: Border.all(
                                                color: _selected.contains(word.id) ? const Color(0xFF1E6FE0) : const Color(0x553F8CFF)),
                                          ),
                                        ),
                                      ),
                                    ),
                                if (_draft case final draft? when draft.isAdd)
                                  DraggableBox(
                                    pageRect: rect,
                                    box: draft.box,
                                    onChanged: (box) => setState(() => draft.box = box),
                                    child: _draftText(draft, rect),
                                  )
                                else if (_draft case final draft?)
                                  Positioned.fromRect(
                                    rect: PageStage.toRect(rect, draft.box).inflate(2),
                                    child: IgnorePointer(
                                      child: DecoratedBox(
                                        decoration: BoxDecoration(
                                            border: Border.all(color: Theme.of(context).colorScheme.primary, width: 2)),
                                      ),
                                    ),
                                  ),
                              ],
                            ),
                          ),
                        ),
                        if (_draft case final draft?)
                          Positioned(
                            left: 0,
                            right: 0,
                            bottom: 0,
                            child: MultiProvider(
                              providers: [Provider.value(value: _edits), Provider.value(value: context.read<FontService>())],
                              child: _TextEditorPanel(
                                key: ObjectKey(draft),
                                documentId: widget.documentId,
                                pageId: widget.pageId,
                                draft: draft,
                                hidden: _panelHidden,
                                opacity: _panelOpacity,
                                onHidden: (hidden) {
                                  FocusScope.of(context).unfocus();
                                  setState(() => _panelHidden = hidden);
                                },
                                onOpacity: (value) => setState(() => _panelOpacity = value),
                                onDraftChanged: () => setState(() {}),
                                onClose: _closeEditor,
                              ),
                            ),
                          ),
                      ]),
                    ),
                  ]),
        bottomNavigationBar: ocr == null || _draft != null
            ? null
            : SafeArea(
                child: Padding(
                  padding: const EdgeInsets.all(8),
                  child: _mode == _Mode.replace && !ocr.isReady
                      ? FilledButton.icon(
                          onPressed: _busy || !ocr.canRequest ? null : _recognise,
                          icon: const Icon(Icons.document_scanner_outlined),
                          label: Text(ocr.isWorking ? l.recognising : l.recogniseText),
                        )
                      : _mode == _Mode.replace
                          ? FilledButton(
                              onPressed: _busy || _selected.isEmpty ? null : _editSelection,
                              child: Text(l.editSelectedWords(_selected.length)),
                            )
                          : const SizedBox.shrink(),
                ),
              ),
      ),
    );
  }

  String _hint(BuildContext context, PageOcr ocr) {
    final l = context.l;
    if (_mode == _Mode.add) return l.addTextHint;
    if (ocr.isReady) return ocr.words.isEmpty ? l.noTextFound : l.replaceHint;
    if (ocr.isWorking) return l.recognising;
    if (ocr.state == 'Failed') return l.recogniseFailed;
    return l.recogniseFirst;
  }
}

/// What the user is about to apply.
class _Draft {
  _Draft({required this.ocrResultId, required this.wordIds, required this.expectedRevisionId,
      required this.originalText, required this.text, required this.box, required this.style});

  /// New text placed at [point]: web defaults (Noto Sans, dark grey, 0.3 × 0.05 box).
  factory _Draft.add(Offset point, Size page) => _Draft(
        ocrResultId: null,
        wordIds: const [],
        expectedRevisionId: null,
        originalText: '',
        text: '',
        box: PageBox(point.dx - .15, point.dy - .025, .3, .05).clamp(),
        style: TextStyleSpec(fontId: 'noto-sans', fontVersion: 'archive-main-regular', fontSize: (24 / page.height).clamp(.004, .2)),
      );

  final String? ocrResultId;
  final List<String> wordIds;
  String? expectedRevisionId;
  final String originalText;
  String text;
  PageBox box;
  TextStyleSpec style;

  /// Loaded font family for drawing the draft on the page (null until the font is available).
  String? family;

  bool get isAdd => wordIds.isEmpty;
}

/// The editing controls, floating over the bottom of the page. It can be hidden to see the whole
/// page, and made see-through so the page behind stays visible while editing.
class _TextEditorPanel extends StatefulWidget {
  const _TextEditorPanel({super.key, required this.documentId, required this.pageId, required this.draft,
      required this.hidden, required this.opacity, required this.onHidden, required this.onOpacity,
      required this.onDraftChanged, required this.onClose});

  final String documentId;
  final String pageId;
  final _Draft draft;
  final bool hidden;
  final double opacity;
  final ValueChanged<bool> onHidden;
  final ValueChanged<double> onOpacity;

  /// Text, style or font changed: the page redraws the draft.
  final VoidCallback onDraftChanged;

  /// Closed; true when the edit was applied.
  final ValueChanged<bool> onClose;

  @override
  State<_TextEditorPanel> createState() => _TextEditorPanelState();
}

class _TextEditorPanelState extends State<_TextEditorPanel> {
  static const _colors = ['#202020', '#000000', '#1565C0', '#C62828', '#FFFFFF'];
  late final _text = TextEditingController(text: widget.draft.text);
  List<FontFace> _fonts = const [];
  bool _more = false;
  bool _showOpacity = false;
  bool _busy = false;
  String _idempotencyKey = newIdempotencyKey();

  _Draft get _draft => widget.draft;
  EditApi get _edits => context.read<EditApi>();

  @override
  void initState() {
    super.initState();
    context.read<FontService>().faces().then((fonts) {
      if (mounted) setState(() => _fonts = fonts);
      _loadFont();
    });
  }

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  FontFace? get _face => _fonts
      .where((f) => f.catalogueId == _draft.style.fontId && f.version == _draft.style.fontVersion)
      .firstOrNull;

  /// The real weight of the chosen face; heavier weights are thickened like the server (2 × 0.009 em per 100).
  int get _faceWeight => _face?.weight ?? 400;

  Future<void> _loadFont() async {
    final face = _face;
    if (face != null && await context.read<FontService>().load(face) && mounted) {
      setState(() => _draft.family = face.webFamilyName);
      widget.onDraftChanged();
    }
  }

  void _update(TextStyleSpec style) {
    setState(() {
      _draft.style = style;
      _idempotencyKey = newIdempotencyKey();
    });
    widget.onDraftChanged();
  }

  void _chooseFamily(String catalogueId) {
    final regular = _fonts.where((f) => f.catalogueId == catalogueId && f.weight == 400).firstOrNull ??
        _fonts.firstWhere((f) => f.catalogueId == catalogueId);
    _update(_draft.style.copyWith(fontId: regular.catalogueId, fontVersion: regular.version));
    _chooseWeight(_draft.style.weight);
    _loadFont();
  }

  /// Any weight 100–900 uses the heaviest real face at or below it (like the web editor).
  void _chooseWeight(int weight) {
    final faces = _fonts.where((f) => f.catalogueId == _draft.style.fontId).toList()
      ..sort((a, b) => b.weight.compareTo(a.weight));
    final face = faces.where((f) => f.weight <= weight).firstOrNull ?? faces.last;
    final clamped = weight < face.weight ? face.weight : weight;
    _update(_draft.style.copyWith(weight: clamped, fontVersion: face.version));
    _loadFont();
  }

  Future<void> _preview() async {
    setState(() => _busy = true);
    try {
      final png = await _edits.previewTextEdit(widget.documentId, widget.pageId,
          ocrResultId: _draft.ocrResultId, expectedRevisionId: _draft.expectedRevisionId, wordIds: _draft.wordIds,
          text: _text.text, box: _draft.box, style: _draft.style);
      if (!mounted) return;
      await showDialog<void>(context: context, builder: (context) => Dialog.fullscreen(
        child: Scaffold(
          appBar: AppBar(title: Text(context.l.exactResult)),
          body: InteractiveViewer(maxScale: 6, child: Center(child: Image.memory(png))),
        ),
      ));
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _apply() async {
    setState(() => _busy = true);
    try {
      final id = await _edits.createTextEdit(widget.documentId, widget.pageId,
          ocrResultId: _draft.ocrResultId, expectedRevisionId: _draft.expectedRevisionId, wordIds: _draft.wordIds,
          text: _text.text, box: _draft.box, style: _draft.style, idempotencyKey: _idempotencyKey);
      for (;;) {
        await Future<void>.delayed(const Duration(milliseconds: 1500));
        final state = await _edits.textEditState(widget.documentId, widget.pageId, id);
        if (state.state == 'Succeeded') break;
        if (state.state == 'Failed') {
          _idempotencyKey = newIdempotencyKey();
          throw ApiException(422, state.failureCode ?? 'text_edit_failed');
        }
      }
      if (mounted) widget.onClose(true);
    } catch (error) {
      if (mounted) showMessage(context, friendlyError(context, error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l = context.l;
    final style = _draft.style;
    final families = <String, FontFace>{};
    for (final face in _fonts) {
      families.putIfAbsent(face.catalogueId, () => face);
    }
    final categories = ['SansSerif', 'Serif', 'Monospace', 'Handwriting'];
    final isReplace = _draft.wordIds.isNotEmpty;
    final canApply = !_busy && (isReplace || _text.text.trim().isNotEmpty);
    final surface = Theme.of(context).colorScheme.surface;
    final header = Row(children: [
      IconButton(tooltip: l.cancel, onPressed: _busy ? null : () => widget.onClose(false), icon: const Icon(Icons.close)),
      const Spacer(),
      IconButton(
        tooltip: l.panelTransparency,
        isSelected: _showOpacity,
        onPressed: () => setState(() => _showOpacity = !_showOpacity),
        icon: const Icon(Icons.opacity),
      ),
      IconButton(
        tooltip: widget.hidden ? l.showEditor : l.hideEditor,
        onPressed: () => widget.onHidden(!widget.hidden),
        icon: Icon(widget.hidden ? Icons.expand_less : Icons.expand_more),
      ),
    ]);
    return Material(
      color: surface.withValues(alpha: widget.opacity),
      elevation: 8,
      borderRadius: const BorderRadius.vertical(top: Radius.circular(16)),
      child: SafeArea(
        top: false,
        child: widget.hidden
            ? Padding(
                padding: const EdgeInsets.fromLTRB(4, 0, 12, 4),
                child: Row(children: [
                  header.children.first,
                  Expanded(
                    child: TextButton.icon(
                      onPressed: () => widget.onHidden(false),
                      icon: const Icon(Icons.expand_less),
                      label: Text(l.showEditor),
                    ),
                  ),
                  FilledButton(onPressed: canApply ? _apply : null, child: Text(l.apply)),
                ]),
              )
            : ConstrainedBox(
                constraints: BoxConstraints(maxHeight: MediaQuery.sizeOf(context).height * .55),
                child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, mainAxisSize: MainAxisSize.min, children: [
          header,
          if (_showOpacity)
            Row(children: [
              Text(l.panelTransparency),
              Expanded(
                child: Slider(
                  value: 1 - widget.opacity,
                  max: .85,
                  onChanged: (value) => widget.onOpacity(1 - value),
                ),
              ),
            ]),
          if (_busy) const LinearProgressIndicator(),
          if (isReplace) Text(l.originalText(_draft.originalText), style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: 8),
          _LivePreview(text: _text.text.isEmpty ? ' ' : _text.text, style: style, family: _face?.webFamilyName,
              faceWeight: _faceWeight),
          const SizedBox(height: 8),
          TextField(
            controller: _text,
            autofocus: true,
            maxLength: 4000,
            decoration: InputDecoration(labelText: isReplace ? l.replacementText : l.newText, border: const OutlineInputBorder()),
            onChanged: (value) {
              setState(() {
                _draft.text = value;
                _idempotencyKey = newIdempotencyKey();
              });
              widget.onDraftChanged();
            },
          ),
          Row(children: [
            Text(l.size),
            Expanded(
              child: Slider(
                value: style.fontSize.clamp(.004, .08),
                min: .004,
                max: .08,
                onChanged: (value) => _update(style.copyWith(fontSize: value)),
              ),
            ),
            for (final color in _colors)
              GestureDetector(
                onTap: () => _update(style.copyWith(colorHex: color)),
                child: Container(
                  margin: const EdgeInsets.all(3),
                  width: 26,
                  height: 26,
                  decoration: BoxDecoration(
                    color: parseHex(color),
                    shape: BoxShape.circle,
                    border: Border.all(width: style.colorHex.toUpperCase() == color ? 3 : 1, color: Colors.grey),
                  ),
                ),
              ),
          ]),
          TextButton.icon(
            onPressed: () => setState(() => _more = !_more),
            icon: Icon(_more ? Icons.expand_less : Icons.expand_more),
            label: Text(l.moreOptions),
          ),
          if (_more) ...[
            DropdownButtonFormField<String>(
              initialValue: families.containsKey(style.fontId) ? style.fontId : null,
              isExpanded: true,
              decoration: InputDecoration(labelText: l.font, border: const OutlineInputBorder()),
              items: [
                for (final category in categories)
                  for (final face in families.values.where((f) => f.category == category))
                    DropdownMenuItem(value: face.catalogueId, child: Text('${face.familyName} · ${_categoryLabel(l, category)}')),
              ],
              onChanged: (value) => value == null ? null : _chooseFamily(value),
            ),
            const SizedBox(height: 12),
            DropdownButtonFormField<int>(
              initialValue: style.weight,
              decoration: InputDecoration(labelText: l.weight, border: const OutlineInputBorder()),
              items: [
                for (var weight = 100; weight <= 900; weight += 100)
                  DropdownMenuItem(value: weight, enabled: weight >= (_fonts.where((f) => f.catalogueId == style.fontId)
                      .map((f) => f.weight).fold<int>(900, (a, b) => a < b ? a : b)), child: Text('$weight')),
              ],
              onChanged: (value) => value == null ? null : _chooseWeight(value),
            ),
            const SizedBox(height: 12),
            Row(children: [
              SegmentedButton<int>(
                segments: const [
                  ButtonSegment(value: 0, icon: Icon(Icons.format_align_left)),
                  ButtonSegment(value: 1, icon: Icon(Icons.format_align_center)),
                  ButtonSegment(value: 2, icon: Icon(Icons.format_align_right)),
                ],
                selected: {style.alignment},
                onSelectionChanged: (value) => _update(style.copyWith(alignment: value.first)),
              ),
              const Spacer(),
              FilterChip(
                label: Text(l.strikethrough),
                selected: style.strikethrough,
                onSelected: (value) => _update(style.copyWith(strikethrough: value)),
              ),
            ]),
            Row(children: [
              Text(l.letterSpacing),
              Expanded(
                child: Slider(
                  value: style.letterSpacing.clamp(-.1, .5),
                  min: -.1,
                  max: .5,
                  onChanged: (value) => _update(style.copyWith(letterSpacing: value)),
                ),
              ),
            ]),
          ],
          const SizedBox(height: 8),
          Row(children: [
            Expanded(child: OutlinedButton(onPressed: _busy ? null : _preview, child: Text(l.preview))),
            const SizedBox(width: 8),
            Expanded(child: FilledButton(onPressed: canApply ? _apply : null, child: Text(l.apply))),
          ]),
        ]),
                ),
              ),
      ),
    );
  }

  static String _categoryLabel(AppLocalizations l, String category) => switch (category) {
        'Serif' => l.fontSerif,
        'Monospace' => l.fontMono,
        'Handwriting' => l.fontHandwriting,
        _ => l.fontSans,
      };
}

/// Live guide: the text in the chosen font, thickened like the server when the weight is above the real face.
class _LivePreview extends StatelessWidget {
  const _LivePreview({required this.text, required this.style, required this.family, required this.faceWeight});

  final String text;
  final TextStyleSpec style;
  final String? family;
  final int faceWeight;

  @override
  Widget build(BuildContext context) {
    const size = 28.0;
    final steps = ((style.weight - faceWeight) / 100).floor().clamp(0, 8);
    final color = parseHex(style.colorHex);
    final base = TextStyle(
      fontFamily: family,
      fontSize: size,
      letterSpacing: style.letterSpacing * size,
      decoration: style.strikethrough ? TextDecoration.lineThrough : null,
      decorationColor: color,
    );
    final align = [TextAlign.left, TextAlign.center, TextAlign.right][style.alignment];
    return Container(
      height: 56,
      padding: const EdgeInsets.symmetric(horizontal: 12),
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: style.colorHex.toUpperCase() == '#FFFFFF' ? Colors.black87 : Colors.white,
        border: Border.all(color: Colors.black12),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Stack(children: [
        if (steps > 0)
          Text(text, maxLines: 1, overflow: TextOverflow.fade, softWrap: false, textAlign: align,
              style: base.copyWith(foreground: Paint()
                ..style = PaintingStyle.stroke
                ..strokeWidth = 2 * .009 * steps * size
                ..color = color)),
        Text(text, maxLines: 1, overflow: TextOverflow.fade, softWrap: false, textAlign: align,
            style: base.copyWith(color: color)),
      ]),
    );
  }
}
