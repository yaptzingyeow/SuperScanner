import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../api/api_client.dart';
import '../../api/edit_api.dart';
import '../common.dart';
import 'page_stage.dart';

/// Eraser: paint over punch holes, staples or debris (or let the server find holes), preview, then apply.
/// Same limits as the web: up to 50 strokes of up to 256 points; small areas only.
class EraserScreen extends StatefulWidget {
  const EraserScreen({super.key, required this.documentId, required this.pageId});

  final String documentId;
  final String pageId;

  @override
  State<EraserScreen> createState() => _EraserScreenState();
}

class _EraserScreenState extends State<EraserScreen> {
  static const _brushes = [.006, .012, .02];
  PageImage? _page;
  Uint8List? _preview;
  String? _operationId;
  bool _showBefore = false;
  bool _busy = false;
  double _radius = _brushes[1];
  final _strokes = <EraseStroke>[];
  final _rectangles = <List<double>>[];
  Object? _error;

  EditApi get _edits => context.read<EditApi>();

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final page = await PageImage.load(context.read<DocumentsApi>(), widget.documentId, widget.pageId);
      if (mounted) setState(() => _page = page);
    } catch (error) {
      if (mounted) setState(() => _error = error);
    }
  }

  void _clearPreview() {
    _preview = null;
    _operationId = null;
  }

  void _startStroke(Offset point) {
    if (_strokes.length >= 50) return;
    setState(() {
      _clearPreview();
      _strokes.add(EraseStroke(_radius)..points.add([point.dx, point.dy]));
    });
  }

  void _extendStroke(Offset point) {
    final stroke = _strokes.lastOrNull;
    if (stroke == null || stroke.points.length >= 256) return;
    final last = stroke.points.last;
    if ((Offset(last[0], last[1]) - point).distance < stroke.radius * .25) return;
    setState(() => stroke.points.add([point.dx, point.dy]));
  }

  Future<String> _waitForPreview(String operationId) async {
    for (;;) {
      await Future<void>.delayed(const Duration(milliseconds: 1200));
      final status = await _edits.getRepair(widget.documentId, widget.pageId, operationId);
      if (status.state != 'Queued') return status.state;
    }
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

  Future<void> _findHoles() => _run(() async {
        final id = await _edits.suggestRepairs(widget.documentId, widget.pageId);
        for (;;) {
          await Future<void>.delayed(const Duration(milliseconds: 1200));
          final status = await _edits.getRepair(widget.documentId, widget.pageId, id);
          if (status.state == 'Queued') continue;
          if (!mounted) return;
          final found = status.candidates.where((box) => box.length == 4).take(20 - _rectangles.length).toList();
          setState(() {
            _clearPreview();
            _rectangles.addAll(found);
          });
          showMessage(context, found.isEmpty ? context.l.noHolesFound : context.l.holesFound(found.length));
          return;
        }
      });

  Future<void> _makePreview() => _run(() async {
        final page = _page!;
        final id = await _edits.createRepairPreview(widget.documentId, widget.pageId,
            previewRevision: page.page.previewRevision, strokes: _strokes, rectangles: _rectangles);
        final state = await _waitForPreview(id);
        if (state != 'Ready') throw const ApiException(500, 'repair_failed');
        final image = await _edits.repairPreviewImage(widget.documentId, widget.pageId, id);
        if (mounted) {
          setState(() {
            _operationId = id;
            _preview = image;
            _showBefore = false;
          });
        }
      });

  Future<void> _apply() => _run(() async {
        await _edits.applyRepair(widget.documentId, widget.pageId, _operationId!);
        if (mounted) Navigator.pop(context, true);
      });

  @override
  Widget build(BuildContext context) {
    final page = _page;
    final l = context.l;
    final hasMarks = _strokes.isNotEmpty || _rectangles.isNotEmpty;
    final showing = _preview != null && !_showBefore ? MemoryImage(_preview!) : (page == null ? null : MemoryImage(page.bytes));
    return Scaffold(
      appBar: AppBar(title: Text(l.toolEraser), actions: [
        IconButton(
          tooltip: l.undoStroke,
          onPressed: _busy || !hasMarks
              ? null
              : () => setState(() {
                    _clearPreview();
                    if (_strokes.isNotEmpty) {
                      _strokes.removeLast();
                    } else {
                      _rectangles.removeLast();
                    }
                  }),
          icon: const Icon(Icons.undo),
        ),
      ]),
      body: _error != null
          ? Center(child: Text(friendlyError(context, _error!)))
          : page == null
              ? const Center(child: CircularProgressIndicator())
              : Column(children: [
                  if (_busy) const LinearProgressIndicator(),
                  Padding(padding: const EdgeInsets.all(8), child: Text(l.eraserHint, textAlign: TextAlign.center)),
                  Expanded(
                    child: PageStage(
                      image: showing!,
                      imageSize: page.size,
                      onPanStart: _busy || _preview != null ? null : _startStroke,
                      onPanUpdate: _busy || _preview != null ? null : _extendStroke,
                      builder: (context, rect) => [
                        if (_preview == null)
                          Positioned.fill(child: IgnorePointer(
                              child: CustomPaint(painter: _MarksPainter(rect, _strokes, _rectangles)))),
                      ],
                    ),
                  ),
                  if (_preview == null)
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 8),
                      child: SegmentedButton<double>(
                        segments: [
                          ButtonSegment(value: _brushes[0], label: Text(l.brushSmall)),
                          ButtonSegment(value: _brushes[1], label: Text(l.brushMedium)),
                          ButtonSegment(value: _brushes[2], label: Text(l.brushLarge)),
                        ],
                        selected: {_radius},
                        onSelectionChanged: (value) => setState(() => _radius = value.first),
                      ),
                    )
                  else
                    SwitchListTile(
                      title: Text(l.showBefore),
                      value: _showBefore,
                      onChanged: (value) => setState(() => _showBefore = value),
                    ),
                ]),
      bottomNavigationBar: page == null
          ? null
          : SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(8),
                child: Row(children: [
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: _busy || _preview != null ? null : _findHoles,
                      icon: const Icon(Icons.auto_fix_high),
                      label: Text(l.findHoles),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: _preview == null
                        ? FilledButton(onPressed: _busy || !hasMarks ? null : _makePreview, child: Text(l.preview))
                        : FilledButton(onPressed: _busy ? null : _apply, child: Text(l.apply)),
                  ),
                ]),
              ),
            ),
    );
  }
}

class _MarksPainter extends CustomPainter {
  _MarksPainter(this.page, this.strokes, this.rectangles);

  final Rect page;
  final List<EraseStroke> strokes;
  final List<List<double>> rectangles;

  @override
  void paint(Canvas canvas, Size size) {
    final fill = Paint()..color = const Color(0x66E53935);
    for (final box in rectangles) {
      canvas.drawRect(Rect.fromLTRB(page.left + box[0] * page.width, page.top + box[1] * page.height,
          page.left + box[2] * page.width, page.top + box[3] * page.height), fill);
    }
    for (final stroke in strokes) {
      // Same as the server: radius is a fraction of the page's shorter side.
      final radius = stroke.radius * page.shortestSide;
      final paint = Paint()
        ..color = const Color(0x66E53935)
        ..strokeWidth = radius * 2
        ..strokeCap = StrokeCap.round
        ..style = PaintingStyle.stroke;
      final points = [for (final p in stroke.points) Offset(page.left + p[0] * page.width, page.top + p[1] * page.height)];
      if (points.length == 1) {
        canvas.drawCircle(points.first, radius, fill);
      } else {
        canvas.drawPath(Path()..addPolygon(points, false), paint);
      }
    }
  }

  @override
  bool shouldRepaint(_MarksPainter old) => true;
}
